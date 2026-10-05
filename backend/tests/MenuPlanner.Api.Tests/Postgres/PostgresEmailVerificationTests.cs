using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Auth.Codes;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Tests.Postgres;

/// <summary>
/// Управляемая конкуренция verify/resend/счётчика попыток и разблокировки на
/// настоящей PostgreSQL: операции встречаются на блокировке строки пользователя,
/// поэтому исход определён кодом, а не случайными задержками.
/// </summary>
public sealed class PostgresEmailVerificationTests : PostgresTestBase
{
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly IPasswordHasher<User> Hasher = new PasswordHasher<User>();

    [PostgresFact]
    public async Task ConcurrentVerify_ConsumesCodeExactlyOnce()
    {
        var code = "123456";
        var user = await SeedUserWithCodeAsync(code, FixedNow.UtcDateTime);

        var barrier = new AsyncBarrier(2);
        var results = await Task.WhenAll(
            VerifyAsync(user.Id, code, barrier),
            VerifyAsync(user.Id, code, barrier));

        Assert.Single(results, r => r.Outcome == VerifyEmailOutcome.Verified);
        Assert.Single(results, r => r.Outcome == VerifyEmailOutcome.AlreadyVerified);

        await using var verify = NewContext();
        Assert.True((await verify.Users.SingleAsync(u => u.Id == user.Id)).IsEmailVerified);
        Assert.Single(await verify.AuthCodes
            .Where(c => c.UserId == user.Id && c.Used)
            .ToListAsync());
    }

    [PostgresFact]
    public async Task ConcurrentWrongAttempts_AreCountedWithoutLoss()
    {
        var user = await SeedUserWithCodeAsync("123456", FixedNow.UtcDateTime);

        var barrier = new AsyncBarrier(5);
        var results = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => VerifyAsync(user.Id, "000000", barrier)));

        Assert.Single(results, r => r.Outcome == VerifyEmailOutcome.Locked);
        Assert.Equal(4, results.Count(r => r.Outcome == VerifyEmailOutcome.InvalidCode));

        await using var verify = NewContext();
        var persisted = await verify.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal(5, persisted.VerificationAttempts);
        Assert.NotNull(persisted.LockedUntil);
    }

    [PostgresFact]
    public async Task ConcurrentResend_WithCooldown_LeavesSingleActiveChallenge()
    {
        var user = await SeedUserWithCodeAsync("123456", FixedNow.UtcDateTime.AddMinutes(-10));
        var options = new AuthCodeOptions(); // cooldown 5 минут

        var barrier = new AsyncBarrier(2);
        var generator = new QueueCodeGenerator("111111", "222222");
        var results = await Task.WhenAll(
            ResendAsync(user.Id, options, generator, barrier),
            ResendAsync(user.Id, options, generator, barrier));

        Assert.Single(results, r => r.Outcome == ResendEmailOutcome.Sent);
        Assert.Single(results, r => r.Outcome == ResendEmailOutcome.TooSoon);

        await using var verify = NewContext();
        Assert.Single(await verify.AuthCodes
            .Where(c => c.UserId == user.Id && !c.Used)
            .ToListAsync());
    }

    [PostgresFact]
    public async Task ConcurrentResend_WithoutCooldown_LeavesSingleActiveChallenge()
    {
        var user = await SeedUserWithCodeAsync("123456", FixedNow.UtcDateTime);
        var options = new AuthCodeOptions { ResendCooldownMinutes = 0 };

        var barrier = new AsyncBarrier(2);
        var generator = new QueueCodeGenerator("111111", "222222");
        var results = await Task.WhenAll(
            ResendAsync(user.Id, options, generator, barrier),
            ResendAsync(user.Id, options, generator, barrier));

        Assert.All(results, r => Assert.Equal(ResendEmailOutcome.Sent, r.Outcome));

        await using var verify = NewContext();
        Assert.Single(await verify.AuthCodes
            .Where(c => c.UserId == user.Id && !c.Used)
            .ToListAsync());
        Assert.Equal(3, await verify.AuthCodes.CountAsync(c => c.UserId == user.Id));
    }

    [PostgresFact]
    public async Task ConcurrentUnlock_DoesNotRestoreLostLock()
    {
        var user = PostgresData.NewUser("race-unlock@example.com");
        user.VerificationAttempts = 5;
        user.LockedUntil = FixedNow.UtcDateTime.AddDays(3);
        await using (var seed = NewContext())
        {
            seed.Users.Add(user);
            seed.AuthCodes.Add(AuthCodeService.Create(
                Hasher, user.Id, AuthCodeType.Verify, "123456", FixedNow.UtcDateTime));
            await seed.SaveChangesAsync();
        }

        var barrier = new AsyncBarrier(2);
        await Task.WhenAll(
            UnlockAsync(user.Id, barrier),
            VerifyAsync(user.Id, "000000", barrier));

        await using var verify = NewContext();
        var persisted = await verify.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Null(persisted.LockedUntil);
        Assert.True(persisted.VerificationAttempts < 5,
            "Разблокировка не должна быть перекрыта утраченной старой блокировкой.");
    }

    private AppDbContext NewContext(BarrierNonQueryInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Database.ConnectionString);
        if (interceptor is not null)
            builder.AddInterceptors(interceptor);
        return new AppDbContext(builder.Options);
    }

    private static EmailVerificationService NewService(AppDbContext db, AuthCodeOptions options, IAuthCodeGenerator generator) =>
        new(db, Hasher, options, generator, new FakeTimeProvider(FixedNow));

    private async Task<User> SeedUserWithCodeAsync(string code, DateTime createdAt)
    {
        var user = PostgresData.NewUser($"race-{Guid.NewGuid():N}@example.com");
        await using var seed = NewContext();
        seed.Users.Add(user);
        seed.AuthCodes.Add(AuthCodeService.Create(Hasher, user.Id, AuthCodeType.Verify, code, createdAt));
        await seed.SaveChangesAsync();
        return user;
    }

    private async Task<VerifyEmailResult> VerifyAsync(Guid userId, string code, AsyncBarrier barrier)
    {
        await using var db = NewContext(new BarrierNonQueryInterceptor(barrier));
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        var service = NewService(db, new AuthCodeOptions(), new QueueCodeGenerator());
        return await service.VerifyAsync(user, code);
    }

    private async Task<ResendEmailResult> ResendAsync(
        Guid userId, AuthCodeOptions options, IAuthCodeGenerator generator, AsyncBarrier barrier)
    {
        await using var db = NewContext(new BarrierNonQueryInterceptor(barrier));
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        var service = NewService(db, options, generator);
        return await service.ResendAsync(user);
    }

    private async Task UnlockAsync(Guid userId, AsyncBarrier barrier)
    {
        await using var db = NewContext(new BarrierNonQueryInterceptor(barrier));
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        var service = NewService(db, new AuthCodeOptions(), new QueueCodeGenerator());
        await service.UnlockAsync(user);
    }
}
