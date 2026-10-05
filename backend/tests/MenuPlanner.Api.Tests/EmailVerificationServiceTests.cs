using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Auth.Codes;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Tests;

public sealed class EmailVerificationServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Verify_WithCorrectCode_ConsumesOnce_AndRepeatsAsAlreadyVerified()
    {
        var (service, db, _, _) = NewService("111111");
        var user = await SeedUserAsync(db);
        await service.IssueInitialCodeAsync(user);

        var first = await service.VerifyAsync(user, "111111");
        Assert.Equal(VerifyEmailOutcome.Verified, first.Outcome);
        Assert.True(user.IsEmailVerified);
        Assert.All(await db.AuthCodes.ToListAsync(), code => Assert.True(code.Used));

        var repeat = await service.VerifyAsync(user, "111111");
        Assert.Equal(VerifyEmailOutcome.AlreadyVerified, repeat.Outcome);
    }

    [Fact]
    public async Task Verify_WithWrongCode_IncrementsAttemptsWithoutConsuming()
    {
        var (service, db, _, _) = NewService("111111");
        var user = await SeedUserAsync(db);
        await service.IssueInitialCodeAsync(user);

        var result = await service.VerifyAsync(user, "000000");

        Assert.Equal(VerifyEmailOutcome.InvalidCode, result.Outcome);
        Assert.Equal(1, user.VerificationAttempts);
        Assert.False(user.IsEmailVerified);
        Assert.All(await db.AuthCodes.ToListAsync(), code => Assert.False(code.Used));
    }

    [Fact]
    public async Task Verify_AfterLifetime_ReportsExpired()
    {
        var (service, db, clock, _) = NewService("111111");
        var user = await SeedUserAsync(db);
        await service.IssueInitialCodeAsync(user);

        clock.Advance(AuthCodeService.VerifyLifetime + TimeSpan.FromSeconds(1));
        var result = await service.VerifyAsync(user, "111111");

        Assert.Equal(VerifyEmailOutcome.ExpiredCode, result.Outcome);
        Assert.False(user.IsEmailVerified);
    }

    [Fact]
    public async Task Verify_WithConsumedCode_ReportsAlreadyUsed()
    {
        var (service, db, _, _) = NewService("111111");
        var user = await SeedUserAsync(db);
        await service.IssueInitialCodeAsync(user);

        var code = await db.AuthCodes.SingleAsync();
        code.Used = true;
        await db.SaveChangesAsync();

        var result = await service.VerifyAsync(user, "111111");

        Assert.Equal(VerifyEmailOutcome.CodeAlreadyUsed, result.Outcome);
    }

    [Fact]
    public async Task Verify_FifthWrongAttempt_LocksOperation()
    {
        var (service, db, _, _) = NewService("111111");
        var user = await SeedUserAsync(db);
        await service.IssueInitialCodeAsync(user);

        VerifyEmailResult result = default;
        for (var i = 0; i < 5; i++)
            result = await service.VerifyAsync(user, "000000");

        Assert.Equal(VerifyEmailOutcome.Locked, result.Outcome);
        Assert.Equal(5, user.VerificationAttempts);
        Assert.NotNull(user.LockedUntil);

        var correctWhileLocked = await service.VerifyAsync(user, "111111");
        Assert.Equal(VerifyEmailOutcome.Locked, correctWhileLocked.Outcome);
    }

    [Fact]
    public async Task Resend_WithinCooldown_ReportsRemainingSeconds()
    {
        var (service, db, _, _) = NewService("111111", "222222");
        var user = await SeedUserAsync(db);
        await service.IssueInitialCodeAsync(user);

        var result = await service.ResendAsync(user);

        Assert.Equal(ResendEmailOutcome.TooSoon, result.Outcome);
        Assert.InRange(result.RetryAfterSeconds, 1, 5 * 60);
    }

    [Fact]
    public async Task Resend_AfterCooldown_IssuesNewCode_AndInvalidatesOld()
    {
        var (service, db, clock, _) = NewService("111111", "222222");
        var user = await SeedUserAsync(db);
        await service.IssueInitialCodeAsync(user);

        clock.Advance(TimeSpan.FromMinutes(new AuthCodeOptions().ResendCooldownMinutes + 1));
        var resend = await service.ResendAsync(user);

        Assert.Equal(ResendEmailOutcome.Sent, resend.Outcome);
        Assert.Equal("222222", resend.Code);

        var oldCode = await service.VerifyAsync(user, "111111");
        Assert.Equal(VerifyEmailOutcome.InvalidCode, oldCode.Outcome);

        var newCode = await service.VerifyAsync(user, "222222");
        Assert.Equal(VerifyEmailOutcome.Verified, newCode.Outcome);
    }

    [Fact]
    public async Task Unlock_ClearsTheSameAttemptsAndLock()
    {
        var (service, db, _, _) = NewService("111111");
        var user = await SeedUserAsync(db);
        await service.IssueInitialCodeAsync(user);
        for (var i = 0; i < 5; i++)
            await service.VerifyAsync(user, "000000");
        Assert.NotNull(user.LockedUntil);

        await service.UnlockAsync(user);

        Assert.Equal(0, user.VerificationAttempts);
        Assert.Null(user.LockedUntil);
    }

    private static (EmailVerificationService Service, AppDbContext Db, FakeTimeProvider Clock, QueueCodeGenerator Codes)
        NewService(params string[] codes)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("verify-svc-" + Guid.NewGuid().ToString("N"))
            .Options;
        var db = new AppDbContext(options);
        var clock = new FakeTimeProvider(Start);
        var generator = new QueueCodeGenerator(codes);
        var service = new EmailVerificationService(
            db, new PasswordHasher<User>(), new AuthCodeOptions(), generator, clock);
        return (service, db, clock, generator);
    }

    private static async Task<User> SeedUserAsync(AppDbContext db)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"verify-{Guid.NewGuid():N}@example.com",
            PasswordHash = "hash",
            CreatedAt = Start.UtcDateTime
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
