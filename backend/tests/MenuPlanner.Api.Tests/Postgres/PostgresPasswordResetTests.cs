using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Auth.Codes;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Tests.Postgres;

/// <summary>
/// Управляемая конкуренция атомарного сброса пароля на настоящей PostgreSQL:
/// потребление кода, счётчик попыток, выдача нового challenge и сценарий
/// запросов без действующего кода. Операции встречаются на блокировке строки
/// пользователя, поэтому исход определён кодом, а не случайными задержками.
/// </summary>
public sealed class PostgresPasswordResetTests : PostgresTestBase
{
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly IPasswordHasher<User> Hasher = new PasswordHasher<User>();

    [PostgresFact]
    public async Task ConcurrentReset_ConsumesCodeExactlyOnce_AndIncrementsVersionOnce()
    {
        var user = await SeedVerifiedUserWithChallengeAsync("123456", FixedNow.UtcDateTime);

        var barrier = new AsyncBarrier(2);
        var results = await Task.WhenAll(
            ResetAsync(user.Id, "123456", "newsecret1", barrier),
            ResetAsync(user.Id, "123456", "newsecret1", barrier));

        Assert.Single(results, r => r.Outcome == PasswordResetOutcome.Reset);
        Assert.Single(results, r => r.Outcome == PasswordResetOutcome.CodeAlreadyUsed);

        await using var verify = NewContext();
        var persisted = await verify.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal(1, persisted.TokenVersion);
        Assert.Equal(0, persisted.VerificationAttempts);
        Assert.Null(persisted.LockedUntil);
        Assert.Single(await verify.AuthCodes
            .Where(c => c.UserId == user.Id && c.Used)
            .ToListAsync());
    }

    [PostgresFact]
    public async Task ConcurrentWrongAttempts_CloseChallenge_WithoutLockingAccount()
    {
        var user = await SeedVerifiedUserWithChallengeAsync("123456", FixedNow.UtcDateTime);

        var barrier = new AsyncBarrier(5);
        var results = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => ResetAsync(user.Id, "000000", "newsecret1", barrier)));

        Assert.Single(results, r => r.Outcome == PasswordResetOutcome.ChallengeClosed);
        Assert.Equal(4, results.Count(r => r.Outcome == PasswordResetOutcome.InvalidCode));

        await using var verify = NewContext();
        var persisted = await verify.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal(0, persisted.VerificationAttempts);
        Assert.Null(persisted.LockedUntil);

        var stored = await verify.AuthCodes.SingleAsync(c => c.UserId == user.Id);
        Assert.True(stored.Used);
        Assert.Equal(5, stored.Attempts);
    }

    [PostgresFact]
    public async Task ConcurrentRequest_WithCooldown_LeavesSingleActiveChallenge()
    {
        var user = await SeedVerifiedUserWithChallengeAsync("123456", FixedNow.UtcDateTime.AddMinutes(-10));
        var options = new AuthCodeOptions(); // cooldown 5 минут

        var barrier = new AsyncBarrier(2);
        var generator = new QueueCodeGenerator("111111", "222222");
        var results = await Task.WhenAll(
            RequestAsync(user.Id, options, generator, barrier),
            RequestAsync(user.Id, options, generator, barrier));

        Assert.Single(results, r => r.Outcome == PasswordResetRequestOutcome.Sent);
        Assert.Single(results, r => r.Outcome == PasswordResetRequestOutcome.TooSoon);

        await using var verify = NewContext();
        Assert.Single(await verify.AuthCodes
            .Where(c => c.UserId == user.Id && !c.Used)
            .ToListAsync());
    }

    [PostgresFact]
    public async Task FiveResetsWithoutChallenge_DoNotLockAccount()
    {
        var user = PostgresData.NewUser($"reset-missing-{Guid.NewGuid():N}@example.com");
        user.IsEmailVerified = true;
        user.EmailVerifiedAt = FixedNow.UtcDateTime;
        await using (var seed = NewContext())
        {
            seed.Users.Add(user);
            await seed.SaveChangesAsync();
        }

        var barrier = new AsyncBarrier(5);
        var results = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => ResetAsync(user.Id, "000000", "newsecret1", barrier)));

        Assert.All(results, r => Assert.Equal(PasswordResetOutcome.InvalidCode, r.Outcome));

        await using var verify = NewContext();
        var persisted = await verify.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal(0, persisted.VerificationAttempts);
        Assert.Null(persisted.LockedUntil);
        Assert.Empty(await verify.AuthCodes.Where(c => c.UserId == user.Id).ToListAsync());
    }

    private AppDbContext NewContext(BarrierNonQueryInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Database.ConnectionString);
        if (interceptor is not null)
            builder.AddInterceptors(interceptor);
        return new AppDbContext(builder.Options);
    }

    private static PasswordResetService NewService(
        AppDbContext db, AuthCodeOptions options, IAuthCodeGenerator generator) =>
        new(new AuthCodeLifecycle(db, Hasher, generator), Hasher, options, new FakeTimeProvider(FixedNow));

    private async Task<User> SeedVerifiedUserWithChallengeAsync(string code, DateTime createdAt)
    {
        var user = PostgresData.NewUser($"reset-{Guid.NewGuid():N}@example.com");
        user.IsEmailVerified = true;
        user.EmailVerifiedAt = FixedNow.UtcDateTime;
        await using var seed = NewContext();
        seed.Users.Add(user);
        seed.AuthCodes.Add(AuthCodePolicy.Create(Hasher, user.Id, AuthCodeType.Reset, code, createdAt));
        await seed.SaveChangesAsync();
        return user;
    }

    private async Task<PasswordResetResult> ResetAsync(
        Guid userId, string code, string newPassword, AsyncBarrier barrier)
    {
        await using var db = NewContext(new BarrierNonQueryInterceptor(barrier));
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        var service = NewService(db, new AuthCodeOptions(), new QueueCodeGenerator());
        return await service.ResetAsync(user, code, newPassword);
    }

    private async Task<PasswordResetRequestResult> RequestAsync(
        Guid userId, AuthCodeOptions options, IAuthCodeGenerator generator, AsyncBarrier barrier)
    {
        await using var db = NewContext(new BarrierNonQueryInterceptor(barrier));
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        var service = NewService(db, options, generator);
        return await service.RequestAsync(user);
    }
}
