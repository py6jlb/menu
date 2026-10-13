using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Plans;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuPlanner.Api.Tests.Plans;

/// <summary>
/// Инвариант принадлежности рецептов семье в <see cref="WeekPlanSaver"/>:
/// полная замена отклоняется исходом <see cref="WeekPlanMutationOutcome.NotOwnedRecipes"/>,
/// не читая ревизию (чужой рецепт — отказ до конфликта) и ничего не записывая.
/// </summary>
public sealed class WeekPlanSaverTests
{
    private static readonly DateOnly Monday = new(2026, 1, 5);

    [Fact]
    public async Task Save_WithOwnedRecipe_ReturnsSuccess()
    {
        await using var db = NewDb();
        var familyId = Guid.NewGuid();
        var recipe = NewRecipe(familyId, "Свой");
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();

        var outcome = await new WeekPlanSaver(db).SaveAsync(
            familyId, Monday,
            new[] { new PlanEntryRequest(0, "breakfast", recipe.Id, 2) },
            WeekPlanRevisions.Initial, DateTime.UtcNow);

        Assert.Equal(WeekPlanMutationOutcome.Success, outcome);
        var plan = await db.WeekPlans.Include(p => p.Entries).SingleAsync(p => p.FamilyId == familyId);
        var entry = Assert.Single(plan.Entries);
        Assert.Equal(recipe.Id, entry.RecipeId);
    }

    [Fact]
    public async Task Save_WithForeignRecipe_ReturnsNotOwnedRecipes_WithoutWriting()
    {
        await using var db = NewDb();
        var familyId = Guid.NewGuid();
        var foreignFamilyId = Guid.NewGuid();
        var foreign = NewRecipe(foreignFamilyId, "Чужой");
        db.Recipes.Add(foreign);
        await db.SaveChangesAsync();

        var outcome = await new WeekPlanSaver(db).SaveAsync(
            familyId, Monday,
            new[] { new PlanEntryRequest(0, "breakfast", foreign.Id, 2) },
            WeekPlanRevisions.Initial, DateTime.UtcNow);

        Assert.Equal(WeekPlanMutationOutcome.NotOwnedRecipes, outcome);
        Assert.Empty(await db.WeekPlans.ToListAsync());
        Assert.Empty(await db.PlanEntries.ToListAsync());
    }

    [Fact]
    public async Task Save_MixOfOwnedAndForeignRecipes_RejectsWholeSet()
    {
        await using var db = NewDb();
        var familyId = Guid.NewGuid();
        var owned = NewRecipe(familyId, "Свой");
        var foreign = NewRecipe(Guid.NewGuid(), "Чужой");
        db.Recipes.AddRange(owned, foreign);
        await db.SaveChangesAsync();

        var outcome = await new WeekPlanSaver(db).SaveAsync(
            familyId, Monday,
            new[]
            {
                new PlanEntryRequest(0, "breakfast", owned.Id, 2),
                new PlanEntryRequest(1, "lunch", foreign.Id, 2)
            },
            WeekPlanRevisions.Initial, DateTime.UtcNow);

        Assert.Equal(WeekPlanMutationOutcome.NotOwnedRecipes, outcome);
        Assert.Empty(await db.WeekPlans.ToListAsync());
    }

    [Fact]
    public async Task Save_WithUnknownRecipeId_ReturnsNotOwnedRecipes()
    {
        await using var db = NewDb();
        var familyId = Guid.NewGuid();

        var outcome = await new WeekPlanSaver(db).SaveAsync(
            familyId, Monday,
            new[] { new PlanEntryRequest(0, "breakfast", Guid.NewGuid(), 2) },
            WeekPlanRevisions.Initial, DateTime.UtcNow);

        Assert.Equal(WeekPlanMutationOutcome.NotOwnedRecipes, outcome);
        Assert.Empty(await db.WeekPlans.ToListAsync());
    }

    [Fact]
    public async Task Save_ForeignRecipeWithStaleRevision_IsNotOwnedNotConflict()
    {
        await using var db = NewDb();
        var familyId = Guid.NewGuid();
        var foreign = NewRecipe(Guid.NewGuid(), "Чужой");
        db.Recipes.Add(foreign);
        db.WeekPlans.Add(NewWeekPlan(familyId, revision: 1));
        await db.SaveChangesAsync();

        // Ревизия заведомо устарела, но принадлежность проверяется первой:
        // исход должен остаться отказом по рецепту, а не конфликтом.
        var outcome = await new WeekPlanSaver(db).SaveAsync(
            familyId, Monday,
            new[] { new PlanEntryRequest(0, "breakfast", foreign.Id, 2) },
            expectedRevision: 0, DateTime.UtcNow);

        Assert.Equal(WeekPlanMutationOutcome.NotOwnedRecipes, outcome);
    }

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static Recipe NewRecipe(Guid familyId, string name) => new()
    {
        Id = Guid.NewGuid(),
        FamilyId = familyId,
        Name = name,
        CookTimeMinutes = 10,
        Servings = 2,
        Difficulty = 1,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static WeekPlan NewWeekPlan(Guid familyId, int revision) => new()
    {
        Id = Guid.NewGuid(),
        FamilyId = familyId,
        WeekStart = Monday,
        Revision = revision,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
