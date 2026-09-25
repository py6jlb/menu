using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
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
        var familyId = await CurrentFamilyIdAsync(principal, db);
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

    private sealed record IngredientGroup(string Normalized, string Name, int Usage);
}
