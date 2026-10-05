using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MenuPlanner.Api.Tests.Postgres;

public sealed class PostgresMigrationTests : PostgresTestBase
{
    // Проверяется именно штатный старт приложения на пустой БД, поэтому
    // базовая инициализация не применяет миграции заранее.
    protected override bool MigrateOnInitialize => false;

    [PostgresFact]
    public async Task EmptyDatabase_IsMigratedByNormalStartup_AndRestartKeepsData()
    {
        Guid userId;

        using (var factory = new PostgresApiFactory(Database.ConnectionString))
        {
            factory.CreateClient();

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.NotEmpty(db.Database.GetMigrations());

            var user = PostgresData.NewUser("migrate@example.com");
            db.Users.Add(user);
            await db.SaveChangesAsync();
            userId = user.Id;
        }

        // Повторный штатный запуск на той же БД: миграции применяются повторно,
        // но не меняют схему и не трогают данные.
        using (var restart = new PostgresApiFactory(Database.ConnectionString))
        {
            restart.CreateClient();

            using var scope = restart.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.Equal(userId, (await db.Users.SingleAsync()).Id);
        }
    }

    [PostgresFact]
    public async Task AppliedMigrations_MatchAllDefinedMigrations_ForFutureUpgrade()
    {
        using var factory = new PostgresApiFactory(Database.ConnectionString);
        factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Сравнение не перечисляет миграции поимённо: следующая миграция,
        // добавленная последующим тикетом, автоматически попадёт в проверку.
        var defined = db.Database.GetMigrations().OrderBy(name => name).ToArray();
        var applied = (await db.Database.GetAppliedMigrationsAsync()).OrderBy(name => name).ToArray();

        Assert.NotEmpty(defined);
        Assert.Equal(defined, applied);
    }
}
