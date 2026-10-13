using System.Security.Claims;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Auth.Sessions;
using MenuPlanner.Api.Recipes;
using MenuPlanner.Api.Recipes.Reading;

namespace MenuPlanner.Api.Ingredients;

/// <summary>Маршруты группы /api/ingredients: автодополнение названий ингредиентов.</summary>
public static class IngredientEndpoints
{
    public static IEndpointRouteBuilder MapIngredientEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ingredients").RequireAuthorization();
        group.MapGet("/autocomplete", AutocompleteAsync);
        return app;
    }

    private static async Task<IResult> AutocompleteAsync(
        string? q, ClaimsPrincipal principal, CurrentUserContext currentUser, RecipeReader recipes)
    {
        var familyId = await currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return Results.Json(new IngredientAutocompleteDto(Array.Empty<IngredientSuggestionDto>()));

        var suggestions = await recipes.ReadIngredientSuggestionsAsync(familyId.Value, q);
        var items = suggestions
            .Select(s => new IngredientSuggestionDto(s.Name, s.Category))
            .ToList();
        return Results.Json(new IngredientAutocompleteDto(items));
    }
}
