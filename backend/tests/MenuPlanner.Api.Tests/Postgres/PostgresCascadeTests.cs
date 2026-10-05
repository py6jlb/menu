using MenuPlanner.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MenuPlanner.Api.Tests.Postgres;

public sealed class PostgresCascadeTests : PostgresTestBase
{
    private const string ForeignKeyViolation = "23503";

    [PostgresFact]
    public async Task DeletingFamily_CascadesMembersRecipesPlanAndShares()
    {
        await using var db = Database.CreateContext();
        var owner = PostgresData.NewUser("owner@example.com");
        var member = PostgresData.NewUser("member@example.com");
        db.Users.AddRange(owner, member);

        var family = PostgresData.NewFamily("Семья", "FAMILY-1", owner.Id);
        db.Families.Add(family);
        db.FamilyMembers.Add(new FamilyMember
        {
            FamilyId = family.Id,
            UserId = member.Id,
            JoinedAt = DateTime.UtcNow
        });

        var recipe = PostgresData.NewRecipe(family.Id, "Суп");
        db.Recipes.Add(recipe);
        db.RecipeSteps.Add(new RecipeStep
        {
            Id = Guid.NewGuid(),
            RecipeId = recipe.Id,
            Order = 0,
            Text = "Варить"
        });
        db.RecipeIngredients.Add(new RecipeIngredient
        {
            Id = Guid.NewGuid(),
            RecipeId = recipe.Id,
            Order = 0,
            Name = "Вода",
            Amount = 1m,
            Unit = "l"
        });
        db.RecipeShares.Add(new RecipeShare
        {
            Id = Guid.NewGuid(),
            RecipeId = recipe.Id,
            Token = "token",
            CreatedAt = DateTime.UtcNow
        });

        var plan = PostgresData.NewWeekPlan(family.Id, new DateOnly(2026, 1, 5));
        db.WeekPlans.Add(plan);
        db.PlanEntries.Add(PostgresData.NewPlanEntry(plan.Id, recipe.Id, 0, MealType.Lunch, 2));
        await db.SaveChangesAsync();

        db.Families.Remove(family);
        await db.SaveChangesAsync();

        Assert.Empty(await db.Families.ToListAsync());
        Assert.Empty(await db.FamilyMembers.ToListAsync());
        Assert.Empty(await db.Recipes.ToListAsync());
        Assert.Empty(await db.RecipeSteps.ToListAsync());
        Assert.Empty(await db.RecipeIngredients.ToListAsync());
        Assert.Empty(await db.RecipeShares.ToListAsync());
        Assert.Empty(await db.WeekPlans.ToListAsync());
        Assert.Empty(await db.PlanEntries.ToListAsync());

        // Пользователи принадлежат приложению, а не семье, и переживают удаление.
        Assert.Equal(2, await db.Users.CountAsync());
    }

    [PostgresFact]
    public async Task DeletingRecipe_CascadesDependentsButKeepsFamilyAndPlan()
    {
        await using var db = Database.CreateContext();
        var owner = PostgresData.NewUser("owner@example.com");
        db.Users.Add(owner);
        var family = PostgresData.NewFamily("Семья", "FAMILY-1", owner.Id);
        db.Families.Add(family);
        var recipe = PostgresData.NewRecipe(family.Id, "Суп");
        db.Recipes.Add(recipe);
        db.RecipeIngredients.Add(new RecipeIngredient
        {
            Id = Guid.NewGuid(),
            RecipeId = recipe.Id,
            Order = 0,
            Name = "Вода",
            Amount = 1m,
            Unit = "l"
        });
        var plan = PostgresData.NewWeekPlan(family.Id, new DateOnly(2026, 1, 5));
        db.WeekPlans.Add(plan);
        db.PlanEntries.Add(PostgresData.NewPlanEntry(plan.Id, recipe.Id, 0, MealType.Lunch, 2));
        await db.SaveChangesAsync();

        db.Recipes.Remove(recipe);
        await db.SaveChangesAsync();

        Assert.Empty(await db.Recipes.ToListAsync());
        Assert.Empty(await db.RecipeIngredients.ToListAsync());
        Assert.Empty(await db.PlanEntries.ToListAsync());
        Assert.Equal(1, await db.Families.CountAsync());
        Assert.Equal(1, await db.WeekPlans.CountAsync());
    }

    [PostgresFact]
    public async Task DeletingFamilyOwner_IsRejectedWhileFamilyExists()
    {
        await using var db = Database.CreateContext();
        var owner = PostgresData.NewUser("owner@example.com");
        db.Users.Add(owner);
        var family = PostgresData.NewFamily("Семья", "FAMILY-1", owner.Id);
        db.Families.Add(family);
        await db.SaveChangesAsync();

        // EF до БД считает обязательную связь с владельцем разорванной и падает
        // концептуально, поэтому внешний ключ Restrict проверяется прямым SQL.
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("DELETE FROM \"Users\" WHERE \"Id\" = {0}", owner.Id));
        Assert.Equal(ForeignKeyViolation, exception.SqlState);
    }
}
