using System.Globalization;
using MenuPlanner.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuPlanner.Api.Tests.Postgres;

public sealed class PostgresIngredientPrecisionTests : PostgresTestBase
{
    [PostgresTheory]
    [InlineData("12345678.91")]
    [InlineData("0.01")]
    [InlineData("0.10")]
    [InlineData("1.50")]
    [InlineData("99999999.99")]
    public async Task Amount_WithinStoredRange_RoundTripsExactly(string raw)
    {
        var amount = decimal.Parse(raw, CultureInfo.InvariantCulture);

        await using (var db = Database.CreateContext())
        {
            var (_, _, recipe) = await PostgresData.SeedFamilyWithRecipeAsync(db);
            db.RecipeIngredients.Add(new RecipeIngredient
            {
                Id = Guid.NewGuid(),
                RecipeId = recipe.Id,
                Order = 0,
                Name = "Мука",
                Amount = amount,
                Unit = "g"
            });
            await db.SaveChangesAsync();
        }

        // Чтение в новом контексте — значение приходит из БД, а не из трекинга.
        await using (var verify = Database.CreateContext())
        {
            var stored = await verify.RecipeIngredients.SingleAsync();
            Assert.Equal(amount, stored.Amount);
        }
    }

    [PostgresTheory]
    [InlineData("100000000")]
    [InlineData("999999999999")]
    public async Task Amount_OutsideStoredRange_IsRejected(string raw)
    {
        var amount = decimal.Parse(raw, CultureInfo.InvariantCulture);

        await using var db = Database.CreateContext();
        var (_, _, recipe) = await PostgresData.SeedFamilyWithRecipeAsync(db);
        db.RecipeIngredients.Add(new RecipeIngredient
        {
            Id = Guid.NewGuid(),
            RecipeId = recipe.Id,
            Order = 0,
            Name = "Мука",
            Amount = amount,
            Unit = "g"
        });

        await PostgresData.AssertPostgresErrorAsync(
            PostgresSqlState.NumericValueOutOfRange, () => db.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task Recipe_LongStrings_AtStorageLimit_RoundTrip()
    {
        var name = new string('Н', RecipeCatalog.NameMaxLength);
        var description = new string('О', RecipeCatalog.TextMaxLength);
        var stepText = new string('Ш', RecipeCatalog.TextMaxLength);
        var ingredientName = new string('И', RecipeCatalog.IngredientNameMaxLength);
        var note = new string('П', RecipeCatalog.NoteMaxLength);

        await using (var db = Database.CreateContext())
        {
            var (_, _, recipe) = await PostgresData.SeedFamilyWithRecipeAsync(db);
            recipe.Name = name;
            recipe.Description = description;
            db.RecipeSteps.Add(new RecipeStep { Id = Guid.NewGuid(), RecipeId = recipe.Id, Order = 0, Text = stepText });
            db.RecipeIngredients.Add(new RecipeIngredient
            {
                Id = Guid.NewGuid(),
                RecipeId = recipe.Id,
                Order = 0,
                Name = ingredientName,
                Amount = 1.25m,
                Unit = "g",
                Note = note
            });
            await db.SaveChangesAsync();
        }

        await using (var verify = Database.CreateContext())
        {
            var stored = await verify.Recipes
                .Include(r => r.Steps)
                .Include(r => r.Ingredients)
                .SingleAsync(r => r.Name == name);
            Assert.Equal(description, stored.Description);
            Assert.Equal(stepText, stored.Steps.Single().Text);
            Assert.Equal(ingredientName, stored.Ingredients.Single().Name);
            Assert.Equal(note, stored.Ingredients.Single().Note);
            Assert.Equal(1.25m, stored.Ingredients.Single().Amount);
        }
    }
}
