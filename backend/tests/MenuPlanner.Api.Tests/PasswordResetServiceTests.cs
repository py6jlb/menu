using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Auth.Codes;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Tests;

public sealed class PasswordResetServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly IPasswordHasher<User> Hasher = new PasswordHasher<User>();

    [Fact]
    public async Task Request_IssuesSingleChallenge_AndRespectsCooldown()
    {
        var (service, db, _, _) = NewService("111111", "222222");
        var user = await SeedVerifiedUserAsync(db);

        var first = await service.RequestAsync(user);
        Assert.Equal(PasswordResetRequestOutcome.Sent, first.Outcome);
        Assert.Equal("111111", first.Code);

        var second = await service.RequestAsync(user);
        Assert.Equal(PasswordResetRequestOutcome.TooSoon, second.Outcome);
        Assert.InRange(second.RetryAfterSeconds, 1, 5 * 60);

        Assert.Single(await db.AuthCodes.ToListAsync());
    }

    [Fact]
    public async Task Request_AfterCooldown_IssuesNewCode_AndInvalidatesOld()
    {
        var (service, db, clock, _) = NewService("111111", "222222");
        var user = await SeedVerifiedUserAsync(db);
        await service.RequestAsync(user);

        clock.Advance(TimeSpan.FromMinutes(new AuthCodeOptions().ResendCooldownMinutes + 1));
        var resend = await service.RequestAsync(user);

        Assert.Equal(PasswordResetRequestOutcome.Sent, resend.Outcome);
        Assert.Equal("222222", resend.Code);
        Assert.Single(await db.AuthCodes.Where(c => !c.Used).ToListAsync());

        var oldCode = await service.ResetAsync(user, "111111", "newsecret1");
        Assert.Equal(PasswordResetOutcome.InvalidCode, oldCode.Outcome);

        var newCode = await service.ResetAsync(user, "222222", "newsecret1");
        Assert.Equal(PasswordResetOutcome.Reset, newCode.Outcome);
    }

    [Fact]
    public async Task Request_ForUnverifiedUser_IsNotEligible_AndIssuesNothing()
    {
        var (service, db, _, _) = NewService("111111");
        var user = await SeedVerifiedUserAsync(db, verified: false);

        var result = await service.RequestAsync(user);

        Assert.Equal(PasswordResetRequestOutcome.NotEligible, result.Outcome);
        Assert.Empty(await db.AuthCodes.ToListAsync());
    }

    [Fact]
    public async Task Request_IgnoresVerificationLock_AndIssuesCode()
    {
        var (service, db, _, _) = NewService("111111");
        var user = await SeedVerifiedUserAsync(db);
        user.VerificationAttempts = AuthCodePolicy.MaxAttempts;
        user.LockedUntil = Start.UtcDateTime.AddDays(3);
        await db.SaveChangesAsync();

        var result = await service.RequestAsync(user);

        Assert.Equal(PasswordResetRequestOutcome.Sent, result.Outcome);
        Assert.Equal(AuthCodePolicy.MaxAttempts, user.VerificationAttempts);
        Assert.NotNull(user.LockedUntil);
    }

    [Fact]
    public async Task Reset_WithCorrectCode_ChangesHash_IncrementsVersion_AndConsumesChallenge()
    {
        var (service, db, _, _) = NewService("111111");
        var user = await SeedVerifiedUserAsync(db, password: "secret1");
        await service.RequestAsync(user);

        var result = await service.ResetAsync(user, "111111", "newsecret1");

        Assert.Equal(PasswordResetOutcome.Reset, result.Outcome);
        Assert.Equal(1, user.TokenVersion);
        Assert.Equal(
            PasswordVerificationResult.Success,
            Hasher.VerifyHashedPassword(user, user.PasswordHash, "newsecret1"));
        Assert.Equal(
            PasswordVerificationResult.Failed,
            Hasher.VerifyHashedPassword(user, user.PasswordHash, "secret1"));

        var stored = await db.AuthCodes.SingleAsync();
        Assert.True(stored.Used);
    }

    [Fact]
    public async Task Reset_WithWrongCode_CountsAttemptsOnChallenge_NotOnUser()
    {
        var (service, db, _, _) = NewService("111111");
        var user = await SeedVerifiedUserAsync(db);
        await service.RequestAsync(user);

        var result = await service.ResetAsync(user, "000000", "newsecret1");

        Assert.Equal(PasswordResetOutcome.InvalidCode, result.Outcome);
        Assert.Equal(0, user.VerificationAttempts);
        Assert.Null(user.LockedUntil);
        Assert.Equal(0, user.TokenVersion);

        var stored = await db.AuthCodes.SingleAsync();
        Assert.False(stored.Used);
        Assert.Equal(1, stored.Attempts);
    }

    [Fact]
    public async Task Reset_FifthWrongAttempt_ClosesChallenge_AndRejectsCorrectCode()
    {
        var (service, db, _, _) = NewService("111111");
        var user = await SeedVerifiedUserAsync(db);
        await service.RequestAsync(user);

        PasswordResetResult result = default;
        for (var i = 0; i < 5; i++)
            result = await service.ResetAsync(user, "000000", "newsecret1");

        Assert.Equal(PasswordResetOutcome.ChallengeClosed, result.Outcome);
        Assert.Equal(0, user.VerificationAttempts);
        Assert.Null(user.LockedUntil);

        var stored = await db.AuthCodes.SingleAsync();
        Assert.True(stored.Used);
        Assert.Equal(5, stored.Attempts);

        var correctAfterClose = await service.ResetAsync(user, "111111", "newsecret1");
        Assert.Equal(PasswordResetOutcome.ChallengeClosed, correctAfterClose.Outcome);
        Assert.Equal(0, user.TokenVersion);
    }

    [Fact]
    public async Task Reset_AfterChallengeClosed_NewCodeAfterCooldownWorks()
    {
        var (service, db, clock, _) = NewService("111111", "222222");
        var user = await SeedVerifiedUserAsync(db);
        await service.RequestAsync(user);
        for (var i = 0; i < 5; i++)
            await service.ResetAsync(user, "000000", "newsecret1");

        clock.Advance(TimeSpan.FromMinutes(new AuthCodeOptions().ResendCooldownMinutes + 1));
        var resend = await service.RequestAsync(user);
        Assert.Equal(PasswordResetRequestOutcome.Sent, resend.Outcome);

        var result = await service.ResetAsync(user, "222222", "newsecret1");
        Assert.Equal(PasswordResetOutcome.Reset, result.Outcome);
        Assert.Equal(1, user.TokenVersion);
    }

    [Fact]
    public async Task Reset_WithoutChallenge_IsInvalid_AndDoesNotLockAccount()
    {
        var (service, db, _, _) = NewService("111111");
        var user = await SeedVerifiedUserAsync(db);

        for (var i = 0; i < 5; i++)
        {
            var result = await service.ResetAsync(user, "000000", "newsecret1");
            Assert.Equal(PasswordResetOutcome.InvalidCode, result.Outcome);
        }

        Assert.Equal(0, user.VerificationAttempts);
        Assert.Null(user.LockedUntil);
        Assert.Empty(await db.AuthCodes.ToListAsync());

        var request = await service.RequestAsync(user);
        Assert.Equal(PasswordResetRequestOutcome.Sent, request.Outcome);
    }

    [Fact]
    public async Task Reset_WithExpiredOrUsedChallenge_DoesNotCountAttemptsNorLock()
    {
        var (service, db, clock, _) = NewService("111111");
        var user = await SeedVerifiedUserAsync(db);
        await service.RequestAsync(user);

        clock.Advance(AuthCodePolicy.ResetLifetime + TimeSpan.FromMinutes(1));
        var expired = await service.ResetAsync(user, "000000", "newsecret1");
        Assert.Equal(PasswordResetOutcome.ExpiredCode, expired.Outcome);

        var stored = await db.AuthCodes.SingleAsync();
        Assert.Equal(0, stored.Attempts);
        Assert.Equal(0, user.VerificationAttempts);
        Assert.Null(user.LockedUntil);
    }

    [Fact]
    public async Task SequentialResets_IncrementTokenVersion()
    {
        var (service, db, clock, _) = NewService("111111", "222222", "333333");
        var user = await SeedVerifiedUserAsync(db);

        await service.RequestAsync(user);
        Assert.Equal(PasswordResetOutcome.Reset,
            (await service.ResetAsync(user, "111111", "newsecret1")).Outcome);

        clock.Advance(TimeSpan.FromMinutes(new AuthCodeOptions().ResendCooldownMinutes + 1));
        await service.RequestAsync(user);
        Assert.Equal(PasswordResetOutcome.Reset,
            (await service.ResetAsync(user, "222222", "newsecret2")).Outcome);

        Assert.Equal(2, user.TokenVersion);
        Assert.Equal(
            PasswordVerificationResult.Success,
            Hasher.VerifyHashedPassword(user, user.PasswordHash, "newsecret2"));
    }

    private static (PasswordResetService Service, AppDbContext Db, FakeTimeProvider Clock, QueueCodeGenerator Codes)
        NewService(params string[] codes)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("reset-svc-" + Guid.NewGuid().ToString("N"))
            .Options;
        var db = new AppDbContext(options);
        var clock = new FakeTimeProvider(Start);
        var generator = new QueueCodeGenerator(codes);
        var service = new PasswordResetService(
            new AuthCodeLifecycle(db, Hasher, generator), Hasher, new AuthCodeOptions(), clock,
            TestOutbox.NewOutbox(db, clock));
        return (service, db, clock, generator);
    }

    private static async Task<User> SeedVerifiedUserAsync(
        AppDbContext db, string password = "secret1", bool verified = true)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"reset-{Guid.NewGuid():N}@example.com",
            PasswordHash = Hasher.HashPassword(null!, password),
            IsEmailVerified = verified,
            EmailVerifiedAt = verified ? Start.UtcDateTime : null,
            CreatedAt = Start.UtcDateTime
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
