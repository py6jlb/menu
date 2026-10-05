using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MenuPlanner.Api.Tests.Postgres;

/// <summary>Строители согласованных данных и проверки ошибок PostgreSQL.</summary>
internal static class PostgresData
{
    public static User NewUser(string email) => new()
    {
        Id = Guid.NewGuid(),
        Email = email,
        PasswordHash = "hash",
        Role = UserRole.User,
        CreatedAt = DateTime.UtcNow
    };

    public static Family NewFamily(string name, string inviteCode, Guid ownerId) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        InviteCode = inviteCode,
        OwnerId = ownerId,
        CreatedAt = DateTime.UtcNow
    };

    public static Recipe NewRecipe(Guid familyId, string name) => new()
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

    public static WeekPlan NewWeekPlan(Guid familyId, DateOnly weekStart) => new()
    {
        Id = Guid.NewGuid(),
        FamilyId = familyId,
        WeekStart = weekStart,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    public static PlanEntry NewPlanEntry(Guid weekPlanId, Guid recipeId, int day, MealType mealType, int portions) => new()
    {
        Id = Guid.NewGuid(),
        WeekPlanId = weekPlanId,
        RecipeId = recipeId,
        Day = day,
        MealType = mealType,
        Portions = portions
    };

    /// <summary>Семья с владельцем — базовый граф для большинства проверок.</summary>
    public static async Task<(User Owner, Family Family)> SeedFamilyAsync(
        AppDbContext db, string email = "owner@example.com", string inviteCode = "FAMILY-1")
    {
        var owner = NewUser(email);
        db.Users.Add(owner);
        var family = NewFamily("Семья", inviteCode, owner.Id);
        db.Families.Add(family);
        await db.SaveChangesAsync();
        return (owner, family);
    }

    /// <summary>Семья с владельцем и рецептом — для проверок, где нужен рецепт.</summary>
    public static async Task<(User Owner, Family Family, Recipe Recipe)> SeedFamilyWithRecipeAsync(
        AppDbContext db, string email = "owner@example.com", string inviteCode = "FAMILY-1")
    {
        var (owner, family) = await SeedFamilyAsync(db, email, inviteCode);
        var recipe = NewRecipe(family.Id, "Суп");
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();
        return (owner, family, recipe);
    }

    public static async Task AssertPostgresErrorAsync(string sqlState, Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<DbUpdateException>(action);
        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(sqlState, postgres.SqlState);
    }
}
