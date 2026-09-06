using System.Globalization;
using System.Linq;
using Xunit;
using MenuPlanner.Api.ShoppingList;

namespace MenuPlanner.Api.Tests;

public sealed class ShoppingListBuilderTests
{
    [Fact]
    public void Aggregates_SameNormalizedName_AcrossLines()
    {
        var items = ShoppingListBuilder.Build(new[]
        {
            new IngredientLine("Мука", 300m, "g"),
            new IngredientLine("мука", 150m, "g")
        });

        var item = Assert.Single(items);
        Assert.Equal("Мука", item.Name);
        Assert.Equal(450m, item.Amount);
        Assert.Equal("g", item.Unit);
        Assert.Equal("450 г", item.Display);
    }

    [Fact]
    public void Scale_DoublesAmount_WhenPortionsAreTwiceServings()
    {
        Assert.Equal(8m, ShoppingListBuilder.Scale(4m, 8, 4));
    }

    [Fact]
    public void Scale_ProducesFractionalAmounts()
    {
        Assert.Equal(1.5m, ShoppingListBuilder.Scale(1m, 6, 4));
    }

    [Fact]
    public void Weight_ConvertsToKilograms_WhenAtLeastOneKilogram()
    {
        var items = ShoppingListBuilder.Build(new[]
        {
            new IngredientLine("мука", 500m, "g"),
            new IngredientLine("мука", 0.5m, "kg")
        });

        var item = Assert.Single(items);
        Assert.Equal(1m, item.Amount);
        Assert.Equal("kg", item.Unit);
        Assert.Equal("1 кг", item.Display);
    }

    [Fact]
    public void Weight_BelowOneKilogram_StaysInGrams()
    {
        var items = ShoppingListBuilder.Build(new[]
        {
            new IngredientLine("сахар", 750m, "g"),
            new IngredientLine("сахар", 0.2m, "kg")
        });

        var item = Assert.Single(items);
        Assert.Equal(950m, item.Amount);
        Assert.Equal("g", item.Unit);
        Assert.Equal("950 г", item.Display);
    }

    [Fact]
    public void Volume_ConvertsToLiters_WhenAtLeastOneLiter()
    {
        var items = ShoppingListBuilder.Build(new[]
        {
            new IngredientLine("молоко", 300m, "ml"),
            new IngredientLine("молоко", 1m, "l")
        });

        var item = Assert.Single(items);
        Assert.Equal(1.3m, item.Amount);
        Assert.Equal("l", item.Unit);
        Assert.Equal("1.3 л", item.Display);
    }

    [Fact]
    public void Volume_BelowOneLiter_StaysInMilliliters()
    {
        var items = ShoppingListBuilder.Build(new[]
        {
            new IngredientLine("вода", 600m, "ml"),
            new IngredientLine("вода", 0.3m, "l")
        });

        var item = Assert.Single(items);
        Assert.Equal(900m, item.Amount);
        Assert.Equal("ml", item.Unit);
        Assert.Equal("900 мл", item.Display);
    }

    [Fact]
    public void Pieces_JustSum()
    {
        var items = ShoppingListBuilder.Build(new[]
        {
            new IngredientLine("яйцо", 2m, "pcs"),
            new IngredientLine("яйцо", 3m, "pcs")
        });

        var item = Assert.Single(items);
        Assert.Equal(5m, item.Amount);
        Assert.Equal("pcs", item.Unit);
        Assert.Equal("5 шт", item.Display);
    }

    [Fact]
    public void HouseholdUnits_AreNotConverted_AndDifferentUnitsStaySeparate()
    {
        var items = ShoppingListBuilder.Build(new[]
        {
            new IngredientLine("соль", 1m, "tbsp"),
            new IngredientLine("соль", 1m, "tsp")
        });

        Assert.Equal(2, items.Count);

        var tbsp = Assert.Single(items, i => i.Unit == "tbsp");
        Assert.Equal(1m, tbsp.Amount);
        Assert.Equal("1 ст. ложка", tbsp.Display);

        var tsp = Assert.Single(items, i => i.Unit == "tsp");
        Assert.Equal("1 ч. ложка", tsp.Display);
    }

    [Fact]
    public void SameHouseholdUnit_SameName_MergesBySummingRawAmounts()
    {
        var items = ShoppingListBuilder.Build(new[]
        {
            new IngredientLine("соль", 1m, "tsp"),
            new IngredientLine("соль", 2m, "tsp")
        });

        var item = Assert.Single(items);
        Assert.Equal(3m, item.Amount);
        Assert.Equal("tsp", item.Unit);
        Assert.Equal("3 ч. ложки", item.Display);
    }

    [Fact]
    public void SameName_DifferentUnitGroups_SeparateLines()
    {
        var items = ShoppingListBuilder.Build(new[]
        {
            new IngredientLine("мука", 300m, "g"),
            new IngredientLine("мука", 1m, "glass")
        });

        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.Unit == "g" && i.Display == "300 г");
        Assert.Contains(items, i => i.Unit == "glass" && i.Display == "1 стакан");
    }

    [Fact]
    public void SameName_WeightAndVolumeGroups_DoNotMerge()
    {
        var items = ShoppingListBuilder.Build(new[]
        {
            new IngredientLine("молоко", 200m, "ml"),
            new IngredientLine("молоко", 100m, "g")
        });

        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void Display_TrimsTrailingZeros()
    {
        var items = ShoppingListBuilder.Build(new[]
        {
            new IngredientLine("яйцо", decimal.Parse("2.50", CultureInfo.InvariantCulture), "pcs")
        });

        var item = Assert.Single(items);
        Assert.Equal(decimal.Parse("2.50", CultureInfo.InvariantCulture), item.Amount);
        Assert.Equal("2.5 шт", item.Display);
    }

    [Fact]
    public void Display_FractionalAmounts_ShownAsIs()
    {
        var items = ShoppingListBuilder.Build(new[]
        {
            new IngredientLine("масло", 0.5m, "glass"),
            new IngredientLine("масло", 1.5m, "glass")
        });

        var item = Assert.Single(items);
        Assert.Equal(2m, item.Amount);
        Assert.Equal("2 стакана", item.Display);
    }

    [Fact]
    public void HouseholdPluralization_UsesRussianForms()
    {
        var items = ShoppingListBuilder.Build(new[]
        {
            new IngredientLine("перец", 1m, "pinch"),
            new IngredientLine("сахар", 5m, "glass"),
            new IngredientLine("мука", 0.5m, "tbsp")
        });

        Assert.Equal("1 щепотка", Assert.Single(items, i => i.Name == "перец").Display);
        Assert.Equal("5 стаканов", Assert.Single(items, i => i.Name == "сахар").Display);
        Assert.Equal("0.5 ст. ложки", Assert.Single(items, i => i.Name == "мука").Display);
    }

    [Fact]
    public void Items_Ordered_ByGroupThenNormalizedName()
    {
        var items = ShoppingListBuilder.Build(new[]
        {
            new IngredientLine("яйцо", 1m, "pcs"),
            new IngredientLine("соль", 1m, "tsp"),
            new IngredientLine("мука", 300m, "g"),
            new IngredientLine("вода", 1m, "l")
        });

        Assert.Equal(new[] { "мука", "вода", "яйцо", "соль" }, items.Select(i => i.Name).ToArray());
    }
}