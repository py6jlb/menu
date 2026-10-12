using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Recipes.External;

namespace MenuPlanner.Api.Recipes;

public static class SharedRecipeEndpoints
{
    public static IEndpointRouteBuilder MapSharedRecipeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/shared/{token}", GetAsync);
        app.MapPost("/api/shared/{token}/import", ImportAsync)
            .RequireAuthorization()
            .RequireVerifiedEmail();

        return app;
    }

    private static async Task<IResult> GetAsync(
        string token, AppDbContext db, SourceFamilyNameResolver sourceNames)
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

        var familyName = await sourceNames.ResolveAsync(recipe.FamilyId);

        // Источник ссылки: отдаём семью-владельца, чтобы фронт мог скрыть
        // «Добавить в мою семью» для участников этой же семьи.
        return Results.Json(RecipeEndpoints.ToDto(
            recipe,
            sourceFamilyName: familyName,
            sourceFamilyId: recipe.FamilyId));
    }

    private static async Task<IResult> ImportAsync(
        string token, ClaimsPrincipal principal, CurrentUserContext currentUser,
        SharedRecipeImportService import)
    {
        var userId = CurrentUser.UserId(principal);
        if (userId is null)
            return Results.Unauthorized();

        var familyId = await currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return Results.NotFound(new RecipeErrorDto("Вы не состоите в семье."));

        return ToResult(await import.ImportAsync(token, familyId.Value));
    }

    private static IResult ToResult(SharedRecipeImportResult result) =>
        result.Outcome switch
        {
            SharedRecipeImportOutcome.Created => Results.Json(
                new RecipeImportResultDto(result.RecipeId),
                statusCode: StatusCodes.Status201Created),
            SharedRecipeImportOutcome.AlreadyImported => Results.Conflict(
                new RecipeImportConflictDto("Рецепт уже добавлен в вашу семью.", result.RecipeId)),
            SharedRecipeImportOutcome.OwnFamily => Results.BadRequest(
                new RecipeErrorDto("Это рецепт вашей семьи.")),
            SharedRecipeImportOutcome.InvalidLink => InvalidLink(),
            _ => throw new InvalidOperationException($"Неизвестный исход: {result.Outcome}.")
        };

    private static IResult InvalidLink() =>
        Results.NotFound(new RecipeErrorDto("Ссылка недействительна."));

}
