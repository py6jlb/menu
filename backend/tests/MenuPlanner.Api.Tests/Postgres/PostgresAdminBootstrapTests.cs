using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Auth.Codes;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Emails;

namespace MenuPlanner.Api.Tests.Postgres;

/// <summary>
/// Bootstrap администратора на настоящей PostgreSQL: конкурентные запуски
/// сериализуются advisory-блокировкой, поэтому ровно один создаёт
/// первоначального администратора, а второй видит уже инициализированное
/// состояние. Обновление установки с существующим Admin ничего не пересоздаёт.
/// </summary>
public sealed class PostgresAdminBootstrapTests : PostgresTestBase
{
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly IPasswordHasher<User> Hasher = new PasswordHasher<User>();

    [PostgresFact]
    public async Task ConcurrentBootstrap_CreatesExactlyOneAdmin()
    {
        var barrier = new AsyncBarrier(2);

        var results = await Task.WhenAll(
            RunAsync("race-a@example.com", "secret1", barrier),
            RunAsync("race-b@example.com", "secret1", barrier));

        Assert.Single(results, r => r.Outcome == AdminBootstrapOutcome.Created);
        Assert.Single(results, r => r.Outcome == AdminBootstrapOutcome.AlreadyInitialized);

        await using var verify = Database.CreateContext();
        Assert.Equal(1, await verify.Users.CountAsync(u => u.Role == UserRole.Admin));
    }

    [PostgresFact]
    public async Task Bootstrap_WithExistingAdmin_PreservesItAndCreatesNoSecond()
    {
        var existing = PostgresData.NewUser("existing-admin@example.com");
        existing.Role = UserRole.Admin;
        await using (var seed = Database.CreateContext())
        {
            seed.Users.Add(existing);
            await seed.SaveChangesAsync();
        }

        var result = await RunAsync("new-admin@example.com", "secret1", null);

        Assert.Equal(AdminBootstrapOutcome.AlreadyInitialized, result.Outcome);

        await using var verify = Database.CreateContext();
        var admins = await verify.Users.Where(u => u.Role == UserRole.Admin).ToListAsync();
        Assert.Single(admins);
        Assert.Equal("existing-admin@example.com", admins[0].Email);
        Assert.False(await verify.Users.AnyAsync(u => u.Email == "new-admin@example.com"));
    }

    private async Task<AdminBootstrapResult> RunAsync(string email, string password, AsyncBarrier? barrier)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Database.ConnectionString);
        if (barrier is not null)
            builder.AddInterceptors(new BarrierNonQueryInterceptor(barrier, "pg_advisory_xact_lock"));

        await using var db = new AppDbContext(builder.Options);
        var clock = new FakeTimeProvider(FixedNow);
        var verification = new EmailVerificationService(
            new AuthCodeLifecycle(db, Hasher, new QueueCodeGenerator("123456")),
            Hasher,
            new AuthCodeOptions(),
            clock);
        var sender = new EmailSender(
            new EmailOptions { From = "noreply@example.com", FromName = "Тест" },
            new RecordingEmailTransport());

        var bootstrap = new AdminBootstrap(
            db, Hasher, verification, sender, clock, NullLogger<AdminBootstrap>.Instance);

        return await bootstrap.RunAsync(email, password);
    }
}
