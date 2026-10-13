using Microsoft.EntityFrameworkCore;

namespace MenuPlanner.Infrastructure.Data;

/// <summary>Применение миграций EF Core на старте приложения.</summary>
public static class DatabaseApplicationBuilderExtensions
{
    /// <summary>
    /// Миграции применяются только для реляционных провайдеров (в тестах —
    /// in-memory, где схему создаёт сам провайдер).
    /// </summary>
    public static async Task UseDatabaseMigration(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (db.Database.IsRelational())
            await db.Database.MigrateAsync();
    }
}
