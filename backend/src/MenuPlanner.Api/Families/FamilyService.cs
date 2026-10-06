using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Families;

/// <summary>Исход операции с семьёй: HTTP-статус выводится из него в endpoint'ах.</summary>
public enum FamilyOutcome
{
    Ok,
    Unauthorized,
    Invalid,
    Conflict,
    NotFound,
    Forbidden
}

/// <summary>Результат операции: исход, при успехе — данные, при ошибке — сообщение.</summary>
public sealed record FamilyAccess(
    FamilyOutcome Outcome,
    FamilyDto? Family = null,
    string? Error = null,
    string? InviteCode = null,
    string? Code = null,
    string? Field = null);

/// <summary>
/// Операции с семьёй поверх БД. Гонка уникального членства (один пользователь —
/// одна семья) разрешается на уровне уникального индекса: проигравший запрос
/// получает ожидаемый конфликт и не оставляет ложного локального состояния.
/// </summary>
public sealed class FamilyService
{
    private const string AlreadyMemberError = "Вы уже состоите в семье.";

    /// <summary>Согласовано с хранилищем: Family.Name — varchar(200).</summary>
    public const int NameMaxLength = 200;

    private readonly AppDbContext _db;

    public FamilyService(AppDbContext db) => _db = db;

    public async Task<FamilyAccess> CreateAsync(string? name, ClaimsPrincipal principal)
    {
        var userId = CurrentUser.UserId(principal);
        if (userId is null)
            return new FamilyAccess(FamilyOutcome.Unauthorized);

        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return new FamilyAccess(
                FamilyOutcome.Invalid, Error: "Укажите название семьи.", Code: "family_name_required", Field: "name");
        if (trimmed.Length > NameMaxLength)
            return new FamilyAccess(
                FamilyOutcome.Invalid,
                Error: $"Название семьи не должно превышать {NameMaxLength} символов.",
                Code: "family_name_too_long",
                Field: "name");

        if (await _db.FamilyMembers.AnyAsync(m => m.UserId == userId.Value))
            return new FamilyAccess(FamilyOutcome.Conflict, Error: AlreadyMemberError);

        var family = new Family
        {
            Id = Guid.NewGuid(),
            Name = trimmed,
            InviteCode = await GenerateUniqueCodeAsync(),
            OwnerId = userId.Value,
            CreatedAt = DateTime.UtcNow
        };
        var membership = new FamilyMember
        {
            FamilyId = family.Id,
            UserId = userId.Value,
            JoinedAt = DateTime.UtcNow
        };
        _db.Families.Add(family);
        _db.FamilyMembers.Add(membership);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Гонка: параллельное создание/вступление уже заняло членство. Отцепляем
            // незаписанные сущности (транзакция откачена) и подтверждаем исход.
            Detach(family, membership);
            if (await _db.FamilyMembers.AsNoTracking().AnyAsync(m => m.UserId == userId.Value))
                return new FamilyAccess(FamilyOutcome.Conflict, Error: AlreadyMemberError);
            throw;
        }

        return new FamilyAccess(FamilyOutcome.Ok, await BuildAsync(family.Id));
    }

    public async Task<FamilyAccess> JoinAsync(string? inviteCode, ClaimsPrincipal principal)
    {
        var userId = CurrentUser.UserId(principal);
        if (userId is null)
            return new FamilyAccess(FamilyOutcome.Unauthorized);

        var code = inviteCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code))
            return new FamilyAccess(FamilyOutcome.Invalid, Error: "Укажите инвайт-код.");

        if (await _db.FamilyMembers.AnyAsync(m => m.UserId == userId.Value))
            return new FamilyAccess(FamilyOutcome.Conflict, Error: AlreadyMemberError);

        var family = await _db.Families.AsNoTracking().SingleOrDefaultAsync(f => f.InviteCode == code);
        if (family is null)
            return new FamilyAccess(FamilyOutcome.NotFound, Error: "Семья по такому коду не найдена.");

        var membership = new FamilyMember
        {
            FamilyId = family.Id,
            UserId = userId.Value,
            JoinedAt = DateTime.UtcNow
        };
        _db.FamilyMembers.Add(membership);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Гонка: параллельное вступление уже создало членство.
            Detach(membership);
            if (await _db.FamilyMembers.AsNoTracking().AnyAsync(m => m.UserId == userId.Value))
                return new FamilyAccess(FamilyOutcome.Conflict, Error: AlreadyMemberError);
            throw;
        }

        return new FamilyAccess(FamilyOutcome.Ok, await BuildAsync(family.Id));
    }

    public async Task<FamilyAccess> GetMyAsync(ClaimsPrincipal principal)
    {
        var userId = CurrentUser.UserId(principal);
        if (userId is null)
            return new FamilyAccess(FamilyOutcome.Unauthorized);

        var membership = await _db.FamilyMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId.Value);
        if (membership is null)
            return new FamilyAccess(FamilyOutcome.NotFound, Error: "Семья не найдена.");

        return new FamilyAccess(FamilyOutcome.Ok, await BuildAsync(membership.FamilyId));
    }

    public async Task<FamilyAccess> RegenerateInviteCodeAsync(Guid id, ClaimsPrincipal principal)
    {
        var userId = CurrentUser.UserId(principal);
        if (userId is null)
            return new FamilyAccess(FamilyOutcome.Unauthorized);

        var family = await _db.Families.FirstOrDefaultAsync(f => f.Id == id);
        if (family is null)
            return new FamilyAccess(FamilyOutcome.NotFound, Error: "Семья не найдена.");

        if (family.OwnerId != userId.Value)
            return new FamilyAccess(FamilyOutcome.Forbidden);

        family.InviteCode = await GenerateUniqueCodeAsync();
        await _db.SaveChangesAsync();

        return new FamilyAccess(FamilyOutcome.Ok, InviteCode: family.InviteCode);
    }

    public async Task<FamilyAccess> RemoveMemberAsync(Guid id, Guid memberId, ClaimsPrincipal principal)
    {
        var callerId = CurrentUser.UserId(principal);
        if (callerId is null)
            return new FamilyAccess(FamilyOutcome.Unauthorized);

        var family = await _db.Families.FirstOrDefaultAsync(f => f.Id == id);
        if (family is null)
            return new FamilyAccess(FamilyOutcome.NotFound, Error: "Семья не найдена.");

        if (family.OwnerId != callerId.Value)
            return new FamilyAccess(FamilyOutcome.Forbidden);

        if (memberId == family.OwnerId)
            return new FamilyAccess(FamilyOutcome.Invalid, Error: "Владелец не может быть удалён из семьи.");

        var membership = await _db.FamilyMembers
            .FirstOrDefaultAsync(m => m.FamilyId == id && m.UserId == memberId);
        if (membership is null)
            return new FamilyAccess(FamilyOutcome.NotFound, Error: "Участник не найден.");

        _db.FamilyMembers.Remove(membership);
        await _db.SaveChangesAsync();

        return new FamilyAccess(FamilyOutcome.Ok);
    }

    private async Task<FamilyDto> BuildAsync(Guid familyId)
    {
        var family = await _db.Families
            .AsNoTracking()
            .SingleAsync(f => f.Id == familyId);
        var members = await _db.FamilyMembers
            .AsNoTracking()
            .Where(m => m.FamilyId == familyId)
            .Include(m => m.User)
            .ToListAsync();

        return FamilyDto.From(family, members);
    }

    private void Detach(params object[] entities)
    {
        foreach (var entity in entities)
            _db.Entry(entity).State = EntityState.Detached;
    }

    private async Task<string> GenerateUniqueCodeAsync()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var code = InviteCodeGenerator.Generate();
            if (!await _db.Families.AnyAsync(f => f.InviteCode == code))
                return code;
        }

        throw new InvalidOperationException("Не удалось сгенерировать уникальный инвайт-код.");
    }
}
