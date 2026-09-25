using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;

namespace MenuPlanner.Api.Recipes;

public static class SharedRecipeEndpoints
{
    public static IEndpointRouteBuilder MapSharedRecipeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/shared/{token}", GetAsync);

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

        return Results.Json(RecipeEndpoints.ToDto(recipe));
    }

    private static IResult InvalidLink() =>
        Results.NotFound(new RecipeErrorDto("Ссылка недействительна."));
}
