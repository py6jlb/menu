using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Data;

namespace MenuPlanner.Api.Ingredients;

public static class IngredientEndpoints
{
    private const int MaxSuggestions = 10;

    public static IEndpointRouteBuilder MapIngredientEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ingredients").RequireAuthorization();
        group.MapGet("/autocomplete", AutocompleteAsync);
        return app;
    }

    private static async Task<IResult> AutocompleteAsync(
        string? q, ClaimsPrincipal principal, AppDbContext db)
    {
        var familyId = await CurrentUser.FamilyIdAsync(principal, db);
        if (familyId is null)
            return Results.Json(new IngredientAutocompleteDto(Array.Empty<string>()));

        var ownNames = await db.Recipes
            .AsNoTracking()
            .Where(r => r.FamilyId == familyId.Value && r.SourceRecipeId == null)
            .SelectMany(r => r.Ingredients)
            .Select(i => i.Name)
            .ToListAsync();

        // Ингредиенты внешних рецептов читаются живьём из источника.
        var sourceIds = await db.Recipes
            .AsNoTracking()
            .Where(r => r.FamilyId == familyId.Value && r.SourceRecipeId != null)
            .Select(r => r.SourceRecipeId!.Value)
            .ToListAsync();

        var externalNames = sourceIds.Count == 0
            ? new List<string>()
            : await db.Recipes
                .AsNoTracking()
                .Where(r => sourceIds.Contains(r.Id))
                .SelectMany(r => r.Ingredients)
                .Select(i => i.Name)
                .ToListAsync();

        var names = ownNames.Concat(externalNames).ToList();

        var normalizedQuery = q?.Trim().ToLowerInvariant() ?? "";

        var groups = names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .GroupBy(n => n.ToLowerInvariant())
            .Select(g => new IngredientGroup(g.Key, g.First(), g.Count()))
            .ToList();

        IEnumerable<IngredientGroup> matches = groups;
        if (normalizedQuery.Length > 0)
            matches = matches.Where(x => x.Normalized.StartsWith(normalizedQuery, StringComparison.Ordinal));

        var items = matches
            .OrderByDescending(x => x.Usage)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSuggestions)
            .Select(x => x.Name)
            .ToList();

        return Results.Json(new IngredientAutocompleteDto(items));
    }

    private sealed record IngredientGroup(string Normalized, string Name, int Usage);
}
