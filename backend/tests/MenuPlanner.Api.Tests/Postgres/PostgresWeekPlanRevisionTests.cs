using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Plans;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuPlanner.Api.Tests.Postgres;

/// <summary>
/// Ревизия недели на настоящей PostgreSQL: устаревшее сохранение/удаление не
/// проходит, две операции от одной ревизии дают один успех и один конфликт,
/// а первое создание недели защищено уникальностью семьи и даты.
/// </summary>
public sealed class PostgresWeekPlanRevisionTests : PostgresTestBase
{
    private static readonly DateOnly Monday = new(2026, 1, 5);

    private AppDbContext NewContext(BarrierNonQueryInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Database.ConnectionString);
        if (interceptor is not null)
            builder.AddInterceptors(interceptor);
        return new AppDbContext(builder.Options);
    }

    private async Task<(Guid FamilyId, Recipe First, Recipe Second)> SeedAsync()
    {
        await using var seed = NewContext();
        var (_, family) = await PostgresData.SeedFamilyAsync(seed, "plan-rev@example.com", "PLAN-REV");
        var first = PostgresData.NewRecipe(family.Id, "Первый");
        var second = PostgresData.NewRecipe(family.Id, "Второй");
        seed.Recipes.AddRange(first, second);
        await seed.SaveChangesAsync();
        return (family.Id, first, second);
    }

    private static Task<WeekPlanMutation> SaveAsync(
        AppDbContext db, Guid familyId, Recipe recipe, int day, int expectedRevision) =>
        new WeekPlanSaver(db).SaveAsync(
            familyId, Monday, new[] { new PlanEntryRequest(day, "lunch", recipe.Id, 2) },
            expectedRevision, DateTime.UtcNow);

    [PostgresFact]
    public async Task FirstSave_CreatesPlanAtRevisionOne()
    {
        var (familyId, first, _) = await SeedAsync();
        await using var db = NewContext();

        Assert.Null(await db.WeekPlans.SingleOrDefaultAsync(p => p.FamilyId == familyId));

        var outcome = await SaveAsync(db, familyId, first, day: 0, expectedRevision: WeekPlanRevisions.Initial);

        Assert.Equal(WeekPlanMutation.Saved, outcome);
        var plan = await db.WeekPlans.SingleAsync(p => p.FamilyId == familyId);
        Assert.Equal(1, plan.Revision);
    }

    [PostgresFact]
    public async Task EmptyExistingPlan_SaveDifferentSlots_ReplacesAndBumpsRevision()
    {
        var (familyId, first, second) = await SeedAsync();
        await using (var seed = NewContext())
        {
            var plan = PostgresData.NewWeekPlan(familyId, Monday);
            plan.Revision = 1;
            seed.WeekPlans.Add(plan);
            await seed.SaveChangesAsync();
        }

        await using var db = NewContext();
        var saver = new WeekPlanSaver(db);
        var outcome = await saver.SaveAsync(
            familyId, Monday,
            new[]
            {
                new PlanEntryRequest(0, "breakfast", first.Id, 2),
                new PlanEntryRequest(4, "dinner", second.Id, 3)
            },
            expectedRevision: 1, DateTime.UtcNow);

        Assert.Equal(WeekPlanMutation.Saved, outcome);
        var saved = await db.WeekPlans.Include(p => p.Entries).SingleAsync(p => p.FamilyId == familyId);
        Assert.Equal(2, saved.Revision);
        Assert.Equal(2, saved.Entries.Count);
    }

    [PostgresFact]
    public async Task ExistingPlan_SaveMatchingSlot_ReplacesEntryWithoutUniqueViolation()
    {
        var (familyId, first, second) = await SeedAsync();
        await using (var seed = NewContext())
        {
            var plan = PostgresData.NewWeekPlan(familyId, Monday);
            plan.Revision = 1;
            seed.WeekPlans.Add(plan);
            await seed.SaveChangesAsync();
            seed.PlanEntries.Add(PostgresData.NewPlanEntry(plan.Id, first.Id, 0, MealType.Lunch, 2));
            await seed.SaveChangesAsync();
        }

        await using var db = NewContext();
        var outcome = await new WeekPlanSaver(db).SaveAsync(
            familyId, Monday, new[] { new PlanEntryRequest(0, "lunch", second.Id, 5) },
            expectedRevision: 1, DateTime.UtcNow);

        Assert.Equal(WeekPlanMutation.Saved, outcome);
        var saved = await db.WeekPlans.Include(p => p.Entries).SingleAsync(p => p.FamilyId == familyId);
        var entry = Assert.Single(saved.Entries);
        Assert.Equal(second.Id, entry.RecipeId);
        Assert.Equal(5, entry.Portions);
    }

    [PostgresFact]
    public async Task ConcurrentSaves_FromSameRevision_YieldOneSuccessOneConflict_WithoutMerge()
    {
        var (familyId, first, second) = await SeedAsync();
        await using (var seed = NewContext())
            Assert.Equal(WeekPlanMutation.Saved,
                await SaveAsync(seed, familyId, first, day: 0, expectedRevision: WeekPlanRevisions.Initial));

        var barrier = new AsyncBarrier(2);
        var results = await Task.WhenAll(
            SaveRaceAsync(familyId, first, day: 1, barrier, "UPDATE \"WeekPlans\""),
            SaveRaceAsync(familyId, second, day: 3, barrier, "UPDATE \"WeekPlans\""));

        Assert.Equal(1, results.Count(r => r == WeekPlanMutation.Saved));
        Assert.Equal(1, results.Count(r => r == WeekPlanMutation.Conflict));

        await using var verify = NewContext();
        var plan = await verify.WeekPlans.Include(p => p.Entries).SingleAsync(p => p.FamilyId == familyId);
        Assert.Equal(2, plan.Revision);
        // Итог — один из двух наборов, а не объединение (иначе было бы две записи).
        var entry = Assert.Single(plan.Entries);
        Assert.Contains(entry.RecipeId, new[] { first.Id, second.Id });
    }

    [PostgresFact]
    public async Task ConcurrentFirstCreation_YieldsOneSuccessOneConflict()
    {
        var (familyId, first, second) = await SeedAsync();
        var barrier = new AsyncBarrier(2);
        var results = await Task.WhenAll(
            SaveRaceAsync(familyId, first, day: 0, barrier, "INSERT INTO \"WeekPlans\"", WeekPlanRevisions.Initial),
            SaveRaceAsync(familyId, second, day: 0, barrier, "INSERT INTO \"WeekPlans\"", WeekPlanRevisions.Initial));

        Assert.Equal(1, results.Count(r => r == WeekPlanMutation.Saved));
        Assert.Equal(1, results.Count(r => r == WeekPlanMutation.Conflict));

        await using var verify = NewContext();
        Assert.Single(await verify.WeekPlans.Where(p => p.FamilyId == familyId).ToListAsync());
    }

    [PostgresFact]
    public async Task Delete_WithMatchingRevision_RemovesPlan_StaleConflicts_IdempotentRepeat()
    {
        var (familyId, first, _) = await SeedAsync();
        await using (var seed = NewContext())
            Assert.Equal(WeekPlanMutation.Saved,
                await SaveAsync(seed, familyId, first, day: 0, expectedRevision: WeekPlanRevisions.Initial));

        await using (var stale = NewContext())
            Assert.Equal(WeekPlanMutation.Conflict,
                await new WeekPlanSaver(stale).DeleteAsync(familyId, Monday, expectedRevision: 0));

        await using (var verify = NewContext())
            Assert.NotNull(await verify.WeekPlans.SingleOrDefaultAsync(p => p.FamilyId == familyId));

        await using (var matching = NewContext())
            Assert.Equal(WeekPlanMutation.Saved,
                await new WeekPlanSaver(matching).DeleteAsync(familyId, Monday, expectedRevision: 1));

        await using (var verify = NewContext())
        {
            Assert.Null(await verify.WeekPlans.SingleOrDefaultAsync(p => p.FamilyId == familyId));
            Assert.Empty(await verify.PlanEntries.ToListAsync());
        }

        // Повторное удаление без новых данных идемпотентно.
        await using (var again = NewContext())
            Assert.Equal(WeekPlanMutation.Saved,
                await new WeekPlanSaver(again).DeleteAsync(familyId, Monday, expectedRevision: 1));
    }

    private async Task<WeekPlanMutation> SaveRaceAsync(
        Guid familyId, Recipe recipe, int day, AsyncBarrier barrier, string fragment, int expectedRevision = 1)
    {
        await using var db = NewContext(new BarrierNonQueryInterceptor(barrier, fragment));
        return await SaveAsync(db, familyId, recipe, day, expectedRevision);
    }
}
