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
}
