using MenuPlanner.Api.Domain;
using Xunit;

namespace MenuPlanner.Api.Tests;

/// <summary>
/// Чистые правила подбора: жёсткие фильтры и мягкие предпочтения не меняются,
/// а поиск по имени применяется до ограничения выдачи и потому находит рецепт
/// за пределами первых <see cref="RecipeMatcher.MaxResults"/>.
/// </summary>
public sealed class RecipeMatcherTests
{
    [Fact]
    public void Apply_SearchBeyondFirstPage_FindsRecipeOutsideLimit()
    {
        var recipes = Enumerable.Range(1, 149)
            .Select(i => NewRecipe($"Рецепт {i:D3}"))
            .Append(NewRecipe("Ягодный пирог"))
            .ToList();

        var withoutSearch = RecipeMatcher.Apply(recipes, null, null);

        Assert.Equal(RecipeMatcher.MaxResults, withoutSearch.Count);
        Assert.DoesNotContain(withoutSearch, m => m.Recipe.Name == "Ягодный пирог");

        var withSearch = RecipeMatcher.Apply(recipes, null, null, "Ягодный");

        Assert.Equal("Ягодный пирог", Assert.Single(withSearch).Recipe.Name);
    }

    [Fact]
    public void Apply_Search_MatchesCaseInsensitivelyAndTrims()
    {
        var recipes = new[] { NewRecipe("Борщ"), NewRecipe("Блины") };

        var matches = RecipeMatcher.Apply(recipes, null, null, "  борщ ");

        Assert.Equal("Борщ", Assert.Single(matches).Recipe.Name);
    }

    [Fact]
    public void Apply_SearchCombinedWithHardFilters_RequiresBoth()
    {
        var recipes = new[]
        {
            NewRecipe("Борщ", difficulty: 4),
            NewRecipe("Борщ лёгкий", difficulty: 1)
        };

        var matches = RecipeMatcher.Apply(
            recipes, new MatchFilters(MaxDifficulty: 2), null, "Борщ");

        Assert.Equal("Борщ лёгкий", Assert.Single(matches).Recipe.Name);
    }

    [Fact]
    public void Apply_EmptySearch_DoesNotDropRecipes()
    {
        var recipes = new[] { NewRecipe("Борщ"), NewRecipe("Блины") };

        var matches = RecipeMatcher.Apply(recipes, null, null, "   ");

        Assert.Equal(2, matches.Count);
    }

    private static Recipe NewRecipe(string name, int difficulty = 1) => new()
    {
        Id = Guid.NewGuid(),
        FamilyId = Guid.NewGuid(),
        Name = name,
        CookTimeMinutes = 10,
        Servings = 2,
        Difficulty = difficulty,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
