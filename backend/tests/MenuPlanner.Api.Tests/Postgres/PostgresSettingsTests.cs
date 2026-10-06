using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Settings;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuPlanner.Api.Tests.Postgres;

/// <summary>
/// Настройки на настоящей PostgreSQL: чтение отсутствующей строки не пишет БД,
/// а конкурентная первая вставка двух сохранений разрешается без необработанного
/// уникального конфликта и оставляет одну строку.
/// </summary>
public sealed class PostgresSettingsTests : PostgresTestBase
{
    [PostgresFact]
    public async Task ReadMissing_DoesNotPersistDefault()
    {
        var user = PostgresData.NewUser(SettingsEmail());
        await using (var seed = Database.CreateContext())
        {
            seed.Users.Add(user);
            await seed.SaveChangesAsync();
        }

        await using var db = Database.CreateContext();
        var service = new SettingsService(db);

        var first = await service.ReadAsync(user.Id);
        var second = await service.ReadAsync(user.Id);

        Assert.Equal(SettingsCatalog.DefaultRepetitionWindowWeeks, first.RepetitionWindowWeeks);
        Assert.Equal(SettingsCatalog.DefaultRepetitionWindowWeeks, second.RepetitionWindowWeeks);
        Assert.False(await db.UserSettings.AnyAsync(s => s.UserId == user.Id));
    }

    [PostgresFact]
    public async Task ConcurrentFirstSave_ResolvesToSingleRow_WithoutError()
    {
        var user = PostgresData.NewUser(SettingsEmail());
        await using (var seed = Database.CreateContext())
        {
            seed.Users.Add(user);
            await seed.SaveChangesAsync();
        }

        var barrier = new AsyncBarrier(2);

        async Task<int> SaveAsync(int weeks)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(Database.ConnectionString)
                .AddInterceptors(new BarrierNonQueryInterceptor(barrier, "INSERT INTO \"UserSettings\""))
                .Options;
            await using var db = new AppDbContext(options);
            var service = new SettingsService(db);
            var saved = await service.SaveAsync(user.Id, weeks);
            return saved.RepetitionWindowWeeks;
        }

        var results = await Task.WhenAll(SaveAsync(4), SaveAsync(9));

        await using var verify = Database.CreateContext();
        var stored = await verify.UserSettings.SingleAsync(s => s.UserId == user.Id);
        Assert.Contains(stored.RepetitionWindowWeeks, new[] { 4, 9 });
        Assert.All(results, value => Assert.Contains(value, new[] { 4, 9 }));
        Assert.Equal(1, await verify.UserSettings.CountAsync(s => s.UserId == user.Id));
    }

    private static string SettingsEmail() => $"settings-{Guid.NewGuid():N}@example.com";
}
