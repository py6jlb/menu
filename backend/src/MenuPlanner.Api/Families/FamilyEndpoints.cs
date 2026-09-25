using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Families;

public static class FamilyEndpoints
{
    public static IEndpointRouteBuilder MapFamilyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/families").RequireAuthorization();

        group.MapPost("/", CreateAsync).RequireVerifiedEmail();
        group.MapGet("/my", GetMyAsync);
        group.MapPost("/join", JoinAsync).RequireVerifiedEmail();
        group.MapPost("/{id:guid}/invite-code/regenerate", RegenerateInviteCodeAsync).RequireVerifiedEmail();
        group.MapDelete("/{id:guid}/members/{userId:guid}", RemoveMemberAsync).RequireVerifiedEmail();

        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateFamilyRequest request,
        ClaimsPrincipal principal,
        AppDbContext db)
    {
        var userId = CurrentUser.UserId(principal);
        if (userId is null)
            return Results.Unauthorized();

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            return Results.BadRequest(new FamilyErrorDto("Укажите название семьи."));

        if (await db.FamilyMembers.AnyAsync(m => m.UserId == userId.Value))
            return Results.Conflict(new FamilyErrorDto("Вы уже состоите в семье."));

        var family = new Family
        {
            Name = name,
            InviteCode = await GenerateUniqueCodeAsync(db),
            OwnerId = userId.Value,
            CreatedAt = DateTime.UtcNow
        };

        db.Families.Add(family);
        db.FamilyMembers.Add(new FamilyMember
        {
            FamilyId = family.Id,
            UserId = userId.Value,
            JoinedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var dto = FamilyDto.From(family, await MembersOfAsync(db, family.Id));
        return Results.Json(dto, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> GetMyAsync(ClaimsPrincipal principal, AppDbContext db)
    {
        var userId = CurrentUser.UserId(principal);
        if (userId is null)
            return Results.Unauthorized();

        var membership = await db.FamilyMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId.Value);
        if (membership is null)
            return Results.NotFound(new FamilyErrorDto("Семья не найдена."));

        var family = await db.Families
            .AsNoTracking()
            .SingleAsync(f => f.Id == membership.FamilyId);

        return Results.Json(FamilyDto.From(family, await MembersOfAsync(db, family.Id)));
    }

    private static async Task<IResult> JoinAsync(
        JoinFamilyRequest request,
        ClaimsPrincipal principal,
        AppDbContext db)
    {
        var userId = CurrentUser.UserId(principal);
        if (userId is null)
            return Results.Unauthorized();

        var code = request.InviteCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code))
            return Results.BadRequest(new FamilyErrorDto("Укажите инвайт-код."));

        if (await db.FamilyMembers.AnyAsync(m => m.UserId == userId.Value))
            return Results.Conflict(new FamilyErrorDto("Вы уже состоите в семье."));

        var family = await db.Families.SingleOrDefaultAsync(f => f.InviteCode == code);
        if (family is null)
            return Results.NotFound(new FamilyErrorDto("Семья по такому коду не найдена."));

        db.FamilyMembers.Add(new FamilyMember
        {
            FamilyId = family.Id,
            UserId = userId.Value,
            JoinedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        return Results.Json(FamilyDto.From(family, await MembersOfAsync(db, family.Id)),
            statusCode: StatusCodes.Status200OK);
    }

    private static async Task<IResult> RegenerateInviteCodeAsync(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext db)
    {
        var userId = CurrentUser.UserId(principal);
        if (userId is null)
            return Results.Unauthorized();

        var family = await db.Families.FirstOrDefaultAsync(f => f.Id == id);
        if (family is null)
            return Results.NotFound(new FamilyErrorDto("Семья не найдена."));

        if (family.OwnerId != userId.Value)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        family.InviteCode = await GenerateUniqueCodeAsync(db);
        await db.SaveChangesAsync();

        return Results.Json(new { inviteCode = family.InviteCode });
    }

    private static async Task<IResult> RemoveMemberAsync(
        Guid id,
        Guid userId,
        ClaimsPrincipal principal,
        AppDbContext db)
    {
        var callerId = CurrentUser.UserId(principal);
        if (callerId is null)
            return Results.Unauthorized();

        var family = await db.Families.FirstOrDefaultAsync(f => f.Id == id);
        if (family is null)
            return Results.NotFound(new FamilyErrorDto("Семья не найдена."));

        if (family.OwnerId != callerId.Value)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        if (userId == family.OwnerId)
            return Results.BadRequest(new FamilyErrorDto("Владелец не может быть удалён из семьи."));

        var membership = await db.FamilyMembers
            .FirstOrDefaultAsync(m => m.FamilyId == id && m.UserId == userId);
        if (membership is null)
            return Results.NotFound(new FamilyErrorDto("Участник не найден."));

        db.FamilyMembers.Remove(membership);
        await db.SaveChangesAsync();

        return Results.NoContent();
    }

    private static async Task<List<FamilyMember>> MembersOfAsync(AppDbContext db, Guid familyId)
    {
        return await db.FamilyMembers
            .AsNoTracking()
            .Where(m => m.FamilyId == familyId)
            .Include(m => m.User)
            .ToListAsync();
    }

    private static async Task<string> GenerateUniqueCodeAsync(AppDbContext db)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var code = InviteCodeGenerator.Generate();
            if (!await db.Families.AnyAsync(f => f.InviteCode == code))
                return code;
        }

        throw new InvalidOperationException("Не удалось сгенерировать уникальный инвайт-код.");
    }
}
