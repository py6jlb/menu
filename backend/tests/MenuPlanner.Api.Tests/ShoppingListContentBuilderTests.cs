using System.Collections.Generic;
using System.Linq;
using Xunit;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Plans;
using MenuPlanner.Api.Recipes.External;
using MenuPlanner.Api.ShoppingList;

namespace MenuPlanner.Api.Tests;

public sealed class ShoppingListContentBuilderTests
{
    private static readonly Guid OwnId = Guid.NewGuid();
    private static readonly Guid WarningId = Guid.NewGuid();
    private static readonly Guid BrokenId = Guid.NewGuid();

    [Fact]
    public void Build_BrokenEntry_IsExcludedWithContext_AndStaleIngredientsAreIgnored()
    {
        var stale = Recipe("Удалённый салат", servings: 2, ("Свёкла", 999m, "pcs"));
        var plan = Plan(Entry(3, "dinner", BrokenId, "Удалённый салат", ExternalRecipeState.Broken, stale));

        var content = ShoppingListContentBuilder.Build(plan);

        Assert.Empty(content.Items);
        var excluded = Assert.Single(content.Excluded);
        Assert.Equal(3, excluded.Day);
        Assert.Equal("dinner", excluded.MealType);
        Assert.Equal(BrokenId, excluded.RecipeId);
        Assert.Equal("Удалённый салат", excluded.RecipeName);
        Assert.Equal(ShoppingListContentBuilder.SourceMissingReason, excluded.Reason);
    }

    [Fact]
    public void Build_MixedOwnWarningBroken_SumsAvailableAndReportsOnlyBroken()
    {
        var plan = Plan(
            Entry(0, "breakfast", OwnId, "Блины", null,
                Recipe("Блины", servings: 1, ("мука", 200m, "g"))),
            Entry(1, "lunch", WarningId, "Салат", ExternalRecipeState.Warning,
                Recipe("Салат", servings: 1, ("мука", 100m, "g"))),
            Entry(2, "dinner", BrokenId, "Пропавший суп", ExternalRecipeState.Broken,
                Recipe("Пропавший суп", servings: 2, ("мука", 500m, "g"))));

        var content = ShoppingListContentBuilder.Build(plan);

        // 200 * 1 + 100 * 1 = 300; вклад сломанной записи не подставлен.
        var flour = Assert.Single(content.Items);
        Assert.Equal("мука", flour.Name);
        Assert.Equal(300m, flour.Amount);

        var excluded = Assert.Single(content.Excluded);
        Assert.Equal(BrokenId, excluded.RecipeId);
        Assert.Equal("dinner", excluded.MealType);
    }

    [Fact]
    public void Build_EmptyPlan_ReturnsNoItemsAndNoExcluded()
    {
        var content = ShoppingListContentBuilder.Build(Plan());

        Assert.Empty(content.Items);
        Assert.Empty(content.Excluded);
    }

    [Fact]
    public void Build_CarriesIngredientCategory_ToAggregatedItem()
    {
        var recipe = new Recipe
        {
            Id = OwnId,
            FamilyId = Guid.NewGuid(),
            Name = "Каша",
            Servings = 1
        };
        recipe.Ingredients.Add(new RecipeIngredient
        {
            Id = Guid.NewGuid(),
            RecipeId = OwnId,
            Order = 0,
            Name = "молоко",
            Amount = 200m,
            Unit = "ml",
            Category = "dairy"
        });
        var plan = Plan(Entry(0, "breakfast", OwnId, "Каша", null, recipe));

        var content = ShoppingListContentBuilder.Build(plan);

        Assert.Equal("dairy", Assert.Single(content.Items).Category);
    }

    private static WeekPlanContent Plan(params PlanContentEntry[] entries) =>
        new(new DateOnly(2026, 9, 7), 1, entries);

    private static PlanContentEntry Entry(
        int day, string meal, Guid recipeId, string name, ExternalRecipeState? state, Recipe? content) =>
        new(day, meal, recipeId, Portions: 1, name, state, content);

    private static Recipe Recipe(
        string name, int servings, params (string Name, decimal Amount, string Unit)[] ingredients)
    {
        var recipe = new Recipe
        {
            Id = Guid.NewGuid(),
            FamilyId = Guid.NewGuid(),
            Name = name,
            Servings = servings
        };
        var order = 0;
        foreach (var (ingredientName, amount, unit) in ingredients)
        {
            recipe.Ingredients.Add(new RecipeIngredient
            {
                Id = Guid.NewGuid(),
                RecipeId = recipe.Id,
                Order = order++,
                Name = ingredientName,
                Amount = amount,
                Unit = unit
            });
        }
        return recipe;
    }
}
