using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Recipes;

public static class SharedRecipeEndpoints
{
    public static IEndpointRouteBuilder MapSharedRecipeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/shared/{token}", GetAsync);
        app.MapPost("/api/shared/{token}/import", ImportAsync).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> GetAsync(string token, AppDbContext db)
    {
        var share = await db.RecipeShares
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Token == token);

        if (share is null || share.RevokedAt is not null)
            return InvalidLink();

        var recipe = await db.Recipes
            .AsNoTracking()
            .Include(r => r.Steps)
            .Include(r => r.Ingredients)
            .FirstOrDefaultAsync(r => r.Id == share.RecipeId);

        if (recipe is null)
            return InvalidLink();

        var familyName = await db.Families
            .AsNoTracking()
            .Where(f => f.Id == recipe.FamilyId)
            .Select(f => f.Name)
            .FirstOrDefaultAsync();

        // Источник ссылки: отдаём семью-владельца, чтобы фронт мог скрыть
        // «Добавить в мою семью» для участников этой же семьи.
        return Results.Json(RecipeEndpoints.ToDto(
            recipe,
            sourceFamilyName: familyName,
            sourceFamilyId: recipe.FamilyId));
    }

    private static async Task<IResult> ImportAsync(
        string token, ClaimsPrincipal principal, AppDbContext db)
    {
        var userId = UserIdFrom(principal);
        if (userId is null)
            return Results.Unauthorized();

        var familyId = await CurrentFamilyIdAsync(principal, db);
        if (familyId is null)
            return Results.NotFound(new RecipeErrorDto("Вы не состоите в семье."));

        var share = await db.RecipeShares
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Token == token);
        if (share is null || share.RevokedAt is not null)
            return InvalidLink();

        var source = await db.Recipes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == share.RecipeId);
        if (source is null)
            return InvalidLink();

        if (source.FamilyId == familyId.Value)
            return Results.BadRequest(new RecipeErrorDto("Это рецепт вашей семьи."));

        var existing = await db.Recipes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.FamilyId == familyId.Value && r.SourceRecipeId == source.Id);
        if (existing is not null)
            return Results.Conflict(new RecipeImportConflictDto(
                "Рецепт уже добавлен в вашу семью.", existing.Id));

        var wrapper = new Recipe
        {
            FamilyId = familyId.Value,
            Name = source.Name,
            SourceRecipeId = source.Id,
            SourceFamilyId = source.FamilyId,
            SourceToken = token,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Recipes.Add(wrapper);
        await db.SaveChangesAsync();

        return Results.Json(
            new RecipeImportResultDto(wrapper.Id, AlreadyAdded: false),
            statusCode: StatusCodes.Status201Created);
    }

    private static IResult InvalidLink() =>
        Results.NotFound(new RecipeErrorDto("Ссылка недействительна."));

    private static async Task<Guid?> CurrentFamilyIdAsync(ClaimsPrincipal principal, AppDbContext db)
    {
        var userId = UserIdFrom(principal);
        if (userId is null)
            return null;

        var membership = await db.FamilyMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId.Value);
        return membership?.FamilyId;
    }

    private static Guid? UserIdFrom(ClaimsPrincipal principal)
    {
        var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}
