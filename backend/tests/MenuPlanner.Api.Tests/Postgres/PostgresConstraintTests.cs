using MenuPlanner.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuPlanner.Api.Tests.Postgres;

public sealed class PostgresConstraintTests : PostgresTestBase
{
    [PostgresFact]
    public async Task FamilyMembership_IsUniquePerUser()
    {
        await using var db = Database.CreateContext();
        var owner = PostgresData.NewUser("owner@example.com");
        var member = PostgresData.NewUser("member@example.com");
        db.Users.AddRange(owner, member);

        var first = PostgresData.NewFamily("Первая", "FAMILY-1", owner.Id);
        var second = PostgresData.NewFamily("Вторая", "FAMILY-2", owner.Id);
        db.Families.AddRange(first, second);
        db.FamilyMembers.Add(new FamilyMember
        {
            FamilyId = first.Id,
            UserId = member.Id,
            JoinedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        db.FamilyMembers.Add(new FamilyMember
        {
            FamilyId = second.Id,
            UserId = member.Id,
            JoinedAt = DateTime.UtcNow
        });
        await PostgresData.AssertPostgresErrorAsync(
            PostgresSqlState.UniqueViolation, () => db.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task Week_IsUniqueWithinFamily()
    {
        await using var db = Database.CreateContext();
        var (_, family) = await PostgresData.SeedFamilyAsync(db);

        var weekStart = new DateOnly(2026, 1, 5);
        db.WeekPlans.Add(PostgresData.NewWeekPlan(family.Id, weekStart));
        await db.SaveChangesAsync();

        db.WeekPlans.Add(PostgresData.NewWeekPlan(family.Id, weekStart));
        await PostgresData.AssertPostgresErrorAsync(
            PostgresSqlState.UniqueViolation, () => db.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task PlanEntry_IsUniquePerWeekDayAndMeal()
    {
        await using var db = Database.CreateContext();
        var (_, family, recipe) = await PostgresData.SeedFamilyWithRecipeAsync(db);
        var plan = PostgresData.NewWeekPlan(family.Id, new DateOnly(2026, 1, 5));
        db.WeekPlans.Add(plan);
        await db.SaveChangesAsync();

        db.PlanEntries.Add(PostgresData.NewPlanEntry(plan.Id, recipe.Id, 0, MealType.Lunch, 2));
        await db.SaveChangesAsync();

        db.PlanEntries.Add(PostgresData.NewPlanEntry(plan.Id, recipe.Id, 0, MealType.Lunch, 3));
        await PostgresData.AssertPostgresErrorAsync(
            PostgresSqlState.UniqueViolation, () => db.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task RecipeShare_IsUniquePerRecipe()
    {
        await using var db = Database.CreateContext();
        var (_, _, recipe) = await PostgresData.SeedFamilyWithRecipeAsync(db);

        db.RecipeShares.Add(new RecipeShare
        {
            Id = Guid.NewGuid(),
            RecipeId = recipe.Id,
            Token = "token-one",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        db.RecipeShares.Add(new RecipeShare
        {
            Id = Guid.NewGuid(),
            RecipeId = recipe.Id,
            Token = "token-two",
            CreatedAt = DateTime.UtcNow
        });
        await PostgresData.AssertPostgresErrorAsync(
            PostgresSqlState.UniqueViolation, () => db.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task RecipeShare_TokenIsUnique()
    {
        await using var db = Database.CreateContext();
        var (_, family, first) = await PostgresData.SeedFamilyWithRecipeAsync(db);
        var second = PostgresData.NewRecipe(family.Id, "Второй");
        db.Recipes.Add(second);
        await db.SaveChangesAsync();

        db.RecipeShares.Add(new RecipeShare
        {
            Id = Guid.NewGuid(),
            RecipeId = first.Id,
            Token = "shared-token",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        db.RecipeShares.Add(new RecipeShare
        {
            Id = Guid.NewGuid(),
            RecipeId = second.Id,
            Token = "shared-token",
            CreatedAt = DateTime.UtcNow
        });
        await PostgresData.AssertPostgresErrorAsync(
            PostgresSqlState.UniqueViolation, () => db.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task ExternalRecipeSource_PartialIndex_AllowsNullsButRejectsDuplicates()
    {
        await using var db = Database.CreateContext();
        var (_, family) = await PostgresData.SeedFamilyAsync(db);

        var sourceId = Guid.NewGuid();

        // У обычных рецептов SourceRecipeId = null — частичный индекс их не ограничивает.
        var plain = PostgresData.NewRecipe(family.Id, "Обычный");
        var anotherPlain = PostgresData.NewRecipe(family.Id, "Ещё обычный");
        db.Recipes.AddRange(plain, anotherPlain);
        await db.SaveChangesAsync();

        var external = PostgresData.NewRecipe(family.Id, "Внешний");
        external.SourceRecipeId = sourceId;
        db.Recipes.Add(external);
        await db.SaveChangesAsync();

        // Другой источник допустим (индекс по паре FamilyId + SourceRecipeId).
        var otherSource = PostgresData.NewRecipe(family.Id, "Другой источник");
        otherSource.SourceRecipeId = Guid.NewGuid();
        db.Recipes.Add(otherSource);
        await db.SaveChangesAsync();

        // Второй внешний рецепт на тот же источник в той же семье запрещён.
        var duplicate = PostgresData.NewRecipe(family.Id, "Дубликат");
        duplicate.SourceRecipeId = sourceId;
        db.Recipes.Add(duplicate);
        await PostgresData.AssertPostgresErrorAsync(
            PostgresSqlState.UniqueViolation, () => db.SaveChangesAsync());
    }
}
