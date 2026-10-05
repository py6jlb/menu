using Microsoft.AspNetCore.Identity;
using Xunit;
using MenuPlanner.Api.Auth.Codes;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Tests;

public sealed class AuthCodeTests
{
    private readonly IPasswordHasher<User> _hasher = new PasswordHasher<User>();

    [Fact]
    public void GenerateCode_ProducesSixDigits()
    {
        var code = AuthCodeService.GenerateCode();

        Assert.Equal(6, code.Length);
        Assert.All(code, ch => Assert.True(char.IsDigit(ch)));
    }

    [Fact]
    public void GenerateCode_ProducesVariedCodes()
    {
        var codes = Enumerable.Range(0, 20).Select(_ => AuthCodeService.GenerateCode()).ToHashSet();

        Assert.True(codes.Count > 1, "Криптографическая генерация не должна повторять один код.");
    }

    [Fact]
    public void CodeMatches_TrueForCorrectCode_FalseForWrong()
    {
        var code = "123456";
        var hash = AuthCodeService.HashCode(_hasher, code);

        Assert.True(AuthCodeService.CodeMatches(_hasher, hash, code));
        Assert.False(AuthCodeService.CodeMatches(_hasher, hash, "654321"));
    }

    [Theory]
    [InlineData(AuthCodeType.Verify, 24)]
    [InlineData(AuthCodeType.Reset, 1)]
    public void LifetimeOf_MatchesSpec(AuthCodeType type, int expectedHours)
    {
        Assert.Equal(TimeSpan.FromHours(expectedHours), AuthCodeService.LifetimeOf(type));
    }

    [Fact]
    public void Check_ReturnsOk_ForValidCode()
    {
        var code = "123456";
        var stored = NewCode(_hasher, code);

        Assert.Equal(CodeCheckResult.Ok, AuthCodeService.Check(_hasher, stored, code, stored.ExpiresAt.AddMinutes(-1)));
    }

    [Fact]
    public void Check_ReturnsInvalid_ForWrongCode()
    {
        var code = "123456";
        var stored = NewCode(_hasher, code);

        Assert.Equal(CodeCheckResult.Invalid, AuthCodeService.Check(_hasher, stored, "000000", DateTime.UtcNow));
    }

    [Fact]
    public void Check_ReturnsExpired_AfterLifetime()
    {
        var code = "123456";
        var stored = NewCode(_hasher, code);

        Assert.Equal(CodeCheckResult.Expired, AuthCodeService.Check(_hasher, stored, code, stored.ExpiresAt));
    }

    [Fact]
    public void Check_ReturnsAlreadyUsed_WhenCodeBurned()
    {
        var code = "123456";
        var stored = NewCode(_hasher, code);
        stored.Used = true;

        Assert.Equal(CodeCheckResult.AlreadyUsed, AuthCodeService.Check(_hasher, stored, code, DateTime.UtcNow));
    }

    [Fact]
    public void RecordFailedAttempt_LocksAfterMaxAttempts()
    {
        var user = NewUser();
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < AuthCodeService.MaxAttempts; i++)
            AuthCodeService.RecordFailedAttempt(user, now);

        Assert.True(AuthCodeService.IsLocked(user, now.AddSeconds(1)));
        Assert.Equal(AuthCodeService.MaxAttempts, user.VerificationAttempts);
    }

    [Fact]
    public void EvaluateLock_ResetsCounterAndLock_WhenExpired()
    {
        var user = NewUser();
        var lockAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < AuthCodeService.MaxAttempts; i++)
            AuthCodeService.RecordFailedAttempt(user, lockAt);

        var afterLock = lockAt.Add(AuthCodeService.LockDuration).AddSeconds(1);
        AuthCodeService.ClearExpiredLock(user, afterLock);

        Assert.False(AuthCodeService.IsLocked(user, afterLock));
        Assert.Equal(0, user.VerificationAttempts);
        Assert.Null(user.LockedUntil);
    }

    [Fact]
    public void Create_SetsTypeHashAndExpiry_FromLifetime()
    {
        var userId = Guid.NewGuid();
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var code = AuthCodeService.Create(_hasher, userId, AuthCodeType.Reset, "123456", now);

        Assert.Equal(userId, code.UserId);
        Assert.Equal(AuthCodeType.Reset, code.Type);
        Assert.False(code.Used);
        Assert.Equal(now, code.CreatedAt);
        Assert.Equal(now.Add(AuthCodeService.ResetLifetime), code.ExpiresAt);
        Assert.True(AuthCodeService.CodeMatches(_hasher, code.CodeHash, "123456"));
    }

    [Fact]
    public void Burn_MarksCodeUsed()
    {
        var code = AuthCodeService.Create(_hasher, Guid.NewGuid(), AuthCodeType.Verify, "123456", DateTime.UtcNow);

        AuthCodeService.Burn(code);

        Assert.True(code.Used);
        Assert.Equal(CodeCheckResult.AlreadyUsed, AuthCodeService.Check(_hasher, code, "123456", DateTime.UtcNow));
    }

    [Fact]
    public void RecordFailedAttempt_DoesNotSlideLock_WhenAlreadyLocked()
    {
        var user = NewUser();
        var lockAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < AuthCodeService.MaxAttempts; i++)
            AuthCodeService.RecordFailedAttempt(user, lockAt);

        Assert.Equal(lockAt.Add(AuthCodeService.LockDuration), user.LockedUntil);

        AuthCodeService.RecordFailedAttempt(user, lockAt.AddMinutes(30));

        Assert.Equal(lockAt.Add(AuthCodeService.LockDuration), user.LockedUntil);
        Assert.Equal(AuthCodeService.MaxAttempts, user.VerificationAttempts);
    }

    [Fact]
    public void ResetAttempts_ClearsCounterAndLock()
    {
        var user = NewUser();
        AuthCodeService.RecordFailedAttempt(user, DateTime.UtcNow);

        AuthCodeService.ResetAttempts(user);

        Assert.Equal(0, user.VerificationAttempts);
        Assert.Null(user.LockedUntil);
    }

    [Fact]
    public void RecordChallengeAttempt_ClosesChallengeAtMaxAttempts()
    {
        var stored = NewCode(_hasher, "123456");

        for (var i = 0; i < AuthCodeService.MaxAttempts - 1; i++)
            Assert.False(AuthCodeService.RecordChallengeAttempt(stored, AuthCodeService.MaxAttempts));

        Assert.False(stored.Used);

        Assert.True(AuthCodeService.RecordChallengeAttempt(stored, AuthCodeService.MaxAttempts));
        Assert.True(stored.Used);
        Assert.Equal(AuthCodeService.MaxAttempts, stored.Attempts);
        Assert.True(AuthCodeService.IsClosedByAttempts(stored, AuthCodeService.MaxAttempts));
    }

    [Fact]
    public void IsClosedByAttempts_FalseForSuccessfullyConsumedCode()
    {
        var stored = NewCode(_hasher, "123456");
        AuthCodeService.RecordChallengeAttempt(stored, AuthCodeService.MaxAttempts);
        AuthCodeService.Burn(stored);

        Assert.False(AuthCodeService.IsClosedByAttempts(stored, AuthCodeService.MaxAttempts));
    }

    [Fact]
    public void RecordChallengeAttempt_ReturnsClosed_ForAlreadyUsedCode()
    {
        var stored = NewCode(_hasher, "123456");
        AuthCodeService.Burn(stored);

        Assert.True(AuthCodeService.RecordChallengeAttempt(stored, AuthCodeService.MaxAttempts));
        Assert.Equal(0, stored.Attempts);
    }

    private static AuthCode NewCode(IPasswordHasher<User> hasher, string code)
    {
        var now = DateTime.UtcNow;
        return new AuthCode
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Type = AuthCodeType.Verify,
            CodeHash = AuthCodeService.HashCode(hasher, code),
            CreatedAt = now,
            ExpiresAt = now.Add(AuthCodeService.LifetimeOf(AuthCodeType.Verify))
        };
    }

    private static User NewUser() => new()
    {
        Id = Guid.NewGuid(),
        Email = "user@example.com",
        PasswordHash = "hash"
    };
}
