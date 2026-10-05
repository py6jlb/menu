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

    public static async Task<PostgresException> AssertPostgresErrorAsync(
        string sqlState, Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<DbUpdateException>(action);
        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(sqlState, postgres.SqlState);
        return postgres;
    }
}
