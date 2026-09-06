namespace MenuPlanner.Api.Domain;

public sealed record MatchFilters(
    int? MaxDifficulty = null,
    int? MaxCalories = null,
    List<string>? Seasons = null,
    List<string>? Diets = null,
    int? MaxCookTimeMinutes = null,
    List<string>? IncludeIngredients = null,
    List<string>? Tags = null);

public sealed record MatchPreferences(
    List<string>? PreferSeasons = null,
    List<string>? PreferDiets = null,
    bool PreferLowCalories = false,
    bool PreferLowComplexity = false);

public sealed record RecipeMatchRequest(
    MatchFilters? Filters = null,
    MatchPreferences? Preferences = null);

public sealed record RecipeMatch(Recipe Recipe, int MatchScore);

public static class RecipeMatcher
{
    public const int MaxResults = 100;

    public static IReadOnlyList<RecipeMatch> Apply(
        IEnumerable<Recipe> source,
        MatchFilters? filters,
        MatchPreferences? preferences)
    {
        filters ??= new MatchFilters();
        preferences ??= new MatchPreferences();

        var ranked = source
            .Where(r => Passes(r, filters))
            .Select(r => new RecipeMatch(r, Score(r, preferences)))
            .OrderByDescending(m => m.MatchScore);

        if (preferences.PreferLowCalories)
            ranked = ranked.ThenBy(m => m.Recipe.Calories ?? int.MaxValue);

        if (preferences.PreferLowComplexity)
            ranked = ranked.ThenBy(m => m.Recipe.Difficulty);

        ranked = ranked
            .ThenBy(m => m.Recipe.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Recipe.Id);

        return ranked.Take(MaxResults).ToList();
    }

    private static bool Passes(Recipe recipe, MatchFilters filters)
    {
        if (filters.MaxDifficulty is { } maxDifficulty && recipe.Difficulty > maxDifficulty)
            return false;

        if (filters.MaxCookTimeMinutes is { } maxCookTime && recipe.CookTimeMinutes > maxCookTime)
            return false;

        if (filters.MaxCalories is { } maxCalories)
        {
            if (recipe.Calories is not { } calories || calories > maxCalories)
                return false;
        }

        if (!Overlaps(recipe.Seasonality, filters.Seasons))
            return false;

        if (!Overlaps(recipe.Diet, filters.Diets))
            return false;

        if (!Overlaps(recipe.Tags, filters.Tags))
            return false;

        if (!MatchesAnyIngredient(recipe.Ingredients, filters.IncludeIngredients))
            return false;

        return true;
    }

    private static int Score(Recipe recipe, MatchPreferences preferences)
    {
        var score = 0;

        if (HasValue(preferences.PreferSeasons) && Overlaps(recipe.Seasonality, preferences.PreferSeasons))
            score++;

        if (HasValue(preferences.PreferDiets) && Overlaps(recipe.Diet, preferences.PreferDiets))
            score++;

        return score;
    }

    private static bool HasValue(IReadOnlyCollection<string>? values) =>
        values is { Count: > 0 };

    private static bool Overlaps(
        IReadOnlyCollection<string>? recipeValues,
        IReadOnlyCollection<string>? requestedValues)
    {
        if (!HasValue(requestedValues))
            return true;

        if (recipeValues is null || recipeValues.Count == 0)
            return false;

        var wanted = NormalizedSet(requestedValues);
        var present = NormalizedSet(recipeValues);
        return wanted.Overlaps(present);
    }

    private static bool MatchesAnyIngredient(
        IEnumerable<RecipeIngredient>? ingredients,
        IReadOnlyCollection<string>? requestedValues)
    {
        if (!HasValue(requestedValues))
            return true;

        if (ingredients is null)
            return false;

        var wanted = NormalizedSet(requestedValues);
        return ingredients.Any(i => wanted.Contains(Normalize(i.Name)));
    }

    private static HashSet<string> NormalizedSet(IEnumerable<string>? values)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (values is null)
            return result;

        foreach (var value in values)
        {
            var normalized = Normalize(value);
            if (normalized.Length > 0)
                result.Add(normalized);
        }

        return result;
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
