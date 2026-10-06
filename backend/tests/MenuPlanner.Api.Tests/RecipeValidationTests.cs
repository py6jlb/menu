using Xunit;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Recipes;

namespace MenuPlanner.Api.Tests;

/// <summary>
/// Чистые правила сохранения рецепта: валидация согласована с decimal-хранилищем,
/// а коллекции и элементы коллекций безопасно отклоняются, не роняя валидатор.
/// </summary>
public sealed class RecipeValidationTests
{
    private static RecipeRequest Valid() => new(
        Name: "Борщ",
        Description: "Классический борщ",
        CookTimeMinutes: 90,
        Servings: 6,
        Difficulty: 3,
        Calories: 350,
        Tags: new List<string> { "суп" },
        Seasonality: new List<string> { "winter" },
        Diet: new List<string>(),
        Steps: new List<RecipeStepRequest> { new("Сварить бульон.") },
        Ingredients: new List<RecipeIngredientRequest> { new("Свёкла", 2m, "pcs", null) });

    [Fact]
    public void Valid_Request_IsAccepted()
    {
        Assert.Null(RecipeValidation.Validate(Valid()));
    }

    [Theory]
    [InlineData("1.50")]
    [InlineData("1.500")]
    [InlineData("99999999.99")]
    [InlineData("0.01")]
    public void Amount_WithAtMostTwoSignificantDecimals_IsAccepted(string raw)
    {
        var request = Valid() with
        {
            Ingredients = new List<RecipeIngredientRequest>
            {
                new("Мука", decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture), "g", null)
            }
        };

        Assert.Null(RecipeValidation.Validate(request));
    }

    [Theory]
    [InlineData("0.001")]
    [InlineData("1.005")]
    [InlineData("2.3456")]
    public void Amount_WithHiddenPrecision_IsRejected(string raw)
    {
        var request = Valid() with
        {
            Ingredients = new List<RecipeIngredientRequest>
            {
                new("Мука", decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture), "g", null)
            }
        };

        var error = RecipeValidation.Validate(request);
        Assert.NotNull(error);
        Assert.Equal("ingredient_amount_precision", error!.Code);
        Assert.Equal("ingredients[0].amount", error.Field);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void Amount_NotPositive_IsRejected(string raw)
    {
        var request = Valid() with
        {
            Ingredients = new List<RecipeIngredientRequest>
            {
                new("Мука", decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture), "g", null)
            }
        };

        var error = RecipeValidation.Validate(request);
        Assert.Equal("ingredient_amount_positive", error!.Code);
    }

    [Fact]
    public void Amount_AboveStoredRange_IsRejected_BeforeWrite()
    {
        var request = Valid() with
        {
            Ingredients = new List<RecipeIngredientRequest> { new("Мука", 100_000_000m, "g", null) }
        };

        var error = RecipeValidation.Validate(request);
        Assert.Equal("ingredient_amount_range", error!.Code);
    }

    [Fact]
    public void Amount_Missing_WithPresentName_IsRejected()
    {
        var request = Valid() with
        {
            Ingredients = new List<RecipeIngredientRequest> { new("Мука", null, "g", null) }
        };

        var error = RecipeValidation.Validate(request);
        Assert.Equal("ingredient_amount_required", error!.Code);
    }

    [Fact]
    public void NullOptionalCollections_AreAccepted()
    {
        var request = Valid() with
        {
            Tags = null,
            Seasonality = null,
            Diet = null,
            Ingredients = null
        };

        Assert.Null(RecipeValidation.Validate(request));
    }

    [Fact]
    public void NullSteps_AreRejectedAsMissing()
    {
        Assert.Equal("steps_required", RecipeValidation.Validate(Valid() with { Steps = null })!.Code);
    }

    [Fact]
    public void NullCollections_AndNullElements_DoNotThrow()
    {
        var request = Valid() with
        {
            Tags = new List<string> { null!, "суп" },
            Seasonality = new List<string> { null!, "winter" },
            Diet = new List<string> { null!, "vegetarian" },
            Steps = new List<RecipeStepRequest> { null!, new("Шаг") },
            Ingredients = new List<RecipeIngredientRequest> { null!, new("Свёкла", 2m, "pcs", null) }
        };

        Assert.Null(RecipeValidation.Validate(request));
    }

    [Fact]
    public void StepsOfOnlyNulls_AreRejectedAsMissing()
    {
        var request = Valid() with
        {
            Steps = new List<RecipeStepRequest> { null!, null! }
        };

        Assert.Equal("steps_required", RecipeValidation.Validate(request)!.Code);
    }

    [Fact]
    public void BlankIngredientRow_IsIgnored()
    {
        var request = Valid() with
        {
            Ingredients = new List<RecipeIngredientRequest>
            {
                new("Свёкла", 2m, "pcs", null),
                new(null, null, null, null)
            }
        };

        Assert.Null(RecipeValidation.Validate(request));
    }

    [Fact]
    public void OverlongStrings_AreRejectedAgainstStoredLimits()
    {
        var longName = new string('а', RecipeCatalog.NameMaxLength + 1);
        Assert.Equal("name_too_long", RecipeValidation.Validate(Valid() with { Name = longName })!.Code);

        var longStep = new string('а', RecipeCatalog.TextMaxLength + 1);
        var longStepRequest = Valid() with
        {
            Steps = new List<RecipeStepRequest> { new(longStep) }
        };
        Assert.Equal("step_too_long", RecipeValidation.Validate(longStepRequest)!.Code);

        var longNote = new string('а', RecipeCatalog.NoteMaxLength + 1);
        var longNoteRequest = Valid() with
        {
            Ingredients = new List<RecipeIngredientRequest> { new("Свёкла", 2m, "pcs", longNote) }
        };
        Assert.Equal("ingredient_note_too_long", RecipeValidation.Validate(longNoteRequest)!.Code);
    }

    [Fact]
    public void ExcessiveCollections_AreRejected()
    {
        var tags = Enumerable.Range(0, RecipeCatalog.TagsMax + 1).Select(i => $"тег{i}").ToList();
        Assert.Equal("tags_too_many", RecipeValidation.Validate(Valid() with { Tags = tags })!.Code);

        var ingredients = Enumerable.Range(0, RecipeCatalog.IngredientsMax + 1)
            .Select(i => new RecipeIngredientRequest($"и{i}", 1m, "g", null))
            .ToList();
        Assert.Equal("ingredients_too_many", RecipeValidation.Validate(Valid() with { Ingredients = ingredients })!.Code);
    }

    [Fact]
    public void MapSteps_AndMapIngredients_SkipNullElements()
    {
        var request = Valid() with
        {
            Steps = new List<RecipeStepRequest> { null!, new("  Шаг  ") },
            Ingredients = new List<RecipeIngredientRequest> { null!, new("  Свёкла ", 2m, "pcs", null) }
        };

        Assert.Equal(new[] { "Шаг" }, RecipeValidation.MapSteps(request).Select(s => s.Text));
        Assert.Equal("Свёкла", RecipeValidation.MapIngredients(request).Single().Name);
    }

    [Fact]
    public void NormalizeStrings_DropsNullsAndKeepsAcceptedMeaning()
    {
        var normalized = RecipeValidation.NormalizeStrings(new List<string> { null!, "  суп ", "суп", "" });

        Assert.Equal(new[] { "суп" }, normalized);
    }
}
