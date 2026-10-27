using MenuPlanner.Infrastructure.Data;
using MenuPlanner.Api.Features.Recipes.Repetition;
using MenuPlanner.Api.Features.Settings;
using Microsoft.EntityFrameworkCore;
using Xunit;
using MenuPlanner.Domain.Accounts;

namespace MenuPlanner.Api.Tests.Features.Plans;

/// <summary>
/// Окно повторяемости <see cref="RepetitionCounter"/> читается через
/// <see cref="SettingsService"/>: сохранённая настройка используется, отсутствие
/// строки и анонимный пользователь дают значение по умолчанию.
/// </summary>
public sealed class RepetitionCounterTests
{
    [Fact]
    public async Task ResolveWindowWeeks_SavedSetting_IsUsed()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        db.UserSettings.Add(new UserSettings { UserId = userId, RepetitionWindowWeeks = 7 });
        await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(7, await NewCounter(db).ResolveWindowWeeksAsync(userId, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResolveWindowWeeks_MissingRow_FallsBackToDefault()
    {
        await using var db = NewDb();

        Assert.Equal(
            SettingsCatalog.DefaultRepetitionWindowWeeks,
            await NewCounter(db).ResolveWindowWeeksAsync(Guid.NewGuid(), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResolveWindowWeeks_Anonymous_FallsBackToDefault()
    {
        await using var db = NewDb();

        Assert.Equal(
            SettingsCatalog.DefaultRepetitionWindowWeeks,
            await NewCounter(db).ResolveWindowWeeksAsync(null, cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>Показатель несёт то же окно, что прочитано из настроек.</summary>
    [Fact]
    public async Task CountWindow_ReportsConfiguredWindowWeeks()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        db.UserSettings.Add(new UserSettings { UserId = userId, RepetitionWindowWeeks = 5 });
        await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await NewCounter(db).CountWindowForUserAsync(userId, Guid.NewGuid(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(5, result.WindowWeeks);
    }

    private static RepetitionCounter NewCounter(AppDbContext db) =>
        new(db, new SettingsService(db));

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
