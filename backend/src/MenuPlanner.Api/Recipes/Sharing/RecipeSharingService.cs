using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Recipes;

/// <summary>Результат операции над ссылкой на рецепт.</summary>
public enum RecipeShareOutcome
{
    Ok,
    Unauthorized,
    RecipeNotFound,
    ExternalReadOnly,
    Forbidden,
    NotCreated
}

/// <summary>
/// Итог операции: исход плюс (при успехе) актуальная ссылка. <see cref="Created"/>
/// отличает первое создание от возврата уже существующей ссылки.
/// </summary>
public sealed record RecipeShareAccess(
    RecipeShareOutcome Outcome,
    RecipeShare? Share = null,
    bool Created = false);

/// <summary>
/// Права и жизненный цикл ссылки на рецепт. Чтение существующей ссылки не создаёт
/// запись; создание доступно любому участнику семьи-источника, отзыв и
/// перегенерация — только Владельцу семьи. Внешний рецепт перешарить нельзя.
/// </summary>
public sealed class RecipeSharingService
{
    private readonly AppDbContext _db;
    private readonly CurrentUserContext _currentUser;
    private readonly TimeProvider _clock;

    public RecipeSharingService(AppDbContext db, CurrentUserContext currentUser, TimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <summary>Прочитать существующую ссылку, ничего не создавая.</summary>
    public async Task<RecipeShareAccess> GetAsync(Guid recipeId, ClaimsPrincipal principal)
    {
        var access = await ResolveMemberAsync(recipeId, principal);
        if (access.Outcome != RecipeShareOutcome.Ok)
            return new(access.Outcome);

        var share = await ReadAsync(recipeId);
        return share is null
            ? new(RecipeShareOutcome.NotCreated)
            : new(RecipeShareOutcome.Ok, share);
    }

    /// <summary>Явно создать ссылку; повторный вызов возвращает уже созданную.</summary>
    public async Task<RecipeShareAccess> CreateAsync(Guid recipeId, ClaimsPrincipal principal)
    {
        var access = await ResolveMemberAsync(recipeId, principal);
        if (access.Outcome != RecipeShareOutcome.Ok)
            return new(access.Outcome);

        var existing = await ReadAsync(recipeId);
        if (existing is not null)
            return new(RecipeShareOutcome.Ok, existing);

        var share = new RecipeShare
        {
            Id = Guid.NewGuid(),
            RecipeId = recipeId,
            Token = NewToken(),
            CreatedAt = _clock.GetUtcNow().UtcDateTime
        };
        _db.RecipeShares.Add(share);

        try
        {
            await _db.SaveChangesAsync();
            return new(RecipeShareOutcome.Ok, share, Created: true);
        }
        catch (DbUpdateException)
        {
            // Гонка первого создания: уникальный индекс по RecipeId не даёт двум
            // запросам выпустить по ссылке. Проигравший отдаёт уже созданную.
            _db.Entry(share).State = EntityState.Detached;
            var concurrent = await ReadAsync(recipeId);
            if (concurrent is not null)
                return new(RecipeShareOutcome.Ok, concurrent);
            throw;
        }
    }

    /// <summary>Отозвать ссылку; повторный отзыв идемпотентен.</summary>
    public async Task<RecipeShareAccess> RevokeAsync(Guid recipeId, ClaimsPrincipal principal)
    {
        var access = await ResolveOwnerAsync(recipeId, principal);
        if (access != RecipeShareOutcome.Ok)
            return new(access);

        var now = _clock.GetUtcNow().UtcDateTime;

        if (_db.SupportsRelationalLocking())
        {
            // Атомарный отзыв: гонка двух отзывов и отзыва с перегенерацией
            // разрешается на уровне строки БД, а не перезаписью состояния.
            await _db.RecipeShares
                .Where(s => s.RecipeId == recipeId && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now));
        }
        else
        {
            var tracked = await _db.RecipeShares.FirstOrDefaultAsync(s => s.RecipeId == recipeId);
            if (tracked is null)
                return new(RecipeShareOutcome.NotCreated);
            if (tracked.RevokedAt is null)
            {
                tracked.RevokedAt = now;
                await _db.SaveChangesAsync();
            }
        }

        var current = await ReadAsync(recipeId);
        return current is null
            ? new(RecipeShareOutcome.NotCreated)
            : new(RecipeShareOutcome.Ok, current);
    }

    /// <summary>Заменить токен; старый становится недействителен.</summary>
    public async Task<RecipeShareAccess> RegenerateAsync(Guid recipeId, ClaimsPrincipal principal)
    {
        var access = await ResolveOwnerAsync(recipeId, principal);
        if (access != RecipeShareOutcome.Ok)
            return new(access);

        if (_db.SupportsRelationalLocking())
        {
            // Перегенерация — условное обновление по наблюдённому токену:
            // проигравшие гонку не выпускают свой токен, а сходятся на актуальном.
            var observed = await ReadAsync(recipeId);
            if (observed is null)
                return new(RecipeShareOutcome.NotCreated);

            var newToken = NewToken();
            await _db.RecipeShares
                .Where(s => s.Id == observed.Id && s.Token == observed.Token)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Token, newToken)
                    .SetProperty(x => x.RevokedAt, (DateTime?)null));

            var current = await ReadAsync(recipeId);
            return current is null
                ? new(RecipeShareOutcome.NotCreated)
                : new(RecipeShareOutcome.Ok, current);
        }

        var tracked = await _db.RecipeShares.FirstOrDefaultAsync(s => s.RecipeId == recipeId);
        if (tracked is null)
            return new(RecipeShareOutcome.NotCreated);

        tracked.Token = NewToken();
        tracked.RevokedAt = null;
        await _db.SaveChangesAsync();
        return new(RecipeShareOutcome.Ok, tracked);
    }

    private Task<RecipeShare?> ReadAsync(Guid recipeId) =>
        _db.RecipeShares
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.RecipeId == recipeId);

    /// <summary>
    /// Проверки участника: авторизация, актуальное членство, рецепт своей семьи
    /// и запрет внешнего рецепта. Возвращает семью и пользователя для проверки владельца.
    /// </summary>
    private async Task<(RecipeShareOutcome Outcome, Guid FamilyId, Guid UserId)> ResolveMemberAsync(
        Guid recipeId, ClaimsPrincipal principal)
    {
        var userId = CurrentUser.UserId(principal);
        if (userId is null)
            return (RecipeShareOutcome.Unauthorized, Guid.Empty, Guid.Empty);

        var familyId = await _currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return (RecipeShareOutcome.RecipeNotFound, Guid.Empty, Guid.Empty);

        var recipe = await _db.Recipes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == recipeId && r.FamilyId == familyId.Value);
        if (recipe is null)
            return (RecipeShareOutcome.RecipeNotFound, Guid.Empty, Guid.Empty);
        if (recipe.SourceRecipeId is not null)
            return (RecipeShareOutcome.ExternalReadOnly, Guid.Empty, Guid.Empty);

        return (RecipeShareOutcome.Ok, familyId.Value, userId.Value);
    }

    private async Task<RecipeShareOutcome> ResolveOwnerAsync(Guid recipeId, ClaimsPrincipal principal)
    {
        var access = await ResolveMemberAsync(recipeId, principal);
        if (access.Outcome != RecipeShareOutcome.Ok)
            return access.Outcome;

        var ownerId = await _db.Families
            .AsNoTracking()
            .Where(f => f.Id == access.FamilyId)
            .Select(f => f.OwnerId)
            .SingleAsync();
        return ownerId == access.UserId
            ? RecipeShareOutcome.Ok
            : RecipeShareOutcome.Forbidden;
    }

    private static string NewToken() => Guid.NewGuid().ToString("N");
}
