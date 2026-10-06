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
        var code = AuthCodePolicy.GenerateCode();

        Assert.Equal(6, code.Length);
        Assert.All(code, ch => Assert.True(char.IsDigit(ch)));
    }

    [Fact]
    public void GenerateCode_ProducesVariedCodes()
    {
        var codes = Enumerable.Range(0, 20).Select(_ => AuthCodePolicy.GenerateCode()).ToHashSet();

        Assert.True(codes.Count > 1, "Криптографическая генерация не должна повторять один код.");
    }

    [Fact]
    public void CodeMatches_TrueForCorrectCode_FalseForWrong()
    {
        var code = "123456";
        var hash = AuthCodePolicy.HashCode(_hasher, code);

        Assert.True(AuthCodePolicy.CodeMatches(_hasher, hash, code));
        Assert.False(AuthCodePolicy.CodeMatches(_hasher, hash, "654321"));
    }

    [Theory]
    [InlineData(AuthCodeType.Verify, 24)]
    [InlineData(AuthCodeType.Reset, 1)]
    public void LifetimeOf_MatchesSpec(AuthCodeType type, int expectedHours)
    {
        Assert.Equal(TimeSpan.FromHours(expectedHours), AuthCodePolicy.LifetimeOf(type));
    }

    [Fact]
    public void Check_ReturnsOk_ForValidCode()
    {
        var code = "123456";
        var stored = NewCode(_hasher, code);

        Assert.Equal(CodeCheckResult.Ok, AuthCodePolicy.Check(_hasher, stored, code, stored.ExpiresAt.AddMinutes(-1)));
    }

    [Fact]
    public void Check_ReturnsInvalid_ForWrongCode()
    {
        var code = "123456";
        var stored = NewCode(_hasher, code);

        Assert.Equal(CodeCheckResult.Invalid, AuthCodePolicy.Check(_hasher, stored, "000000", DateTime.UtcNow));
    }

    [Fact]
    public void Check_ReturnsExpired_AfterLifetime()
    {
        var code = "123456";
        var stored = NewCode(_hasher, code);

        Assert.Equal(CodeCheckResult.Expired, AuthCodePolicy.Check(_hasher, stored, code, stored.ExpiresAt));
    }

    [Fact]
    public void Check_ReturnsAlreadyUsed_WhenCodeBurned()
    {
        var code = "123456";
        var stored = NewCode(_hasher, code);
        stored.Used = true;

        Assert.Equal(CodeCheckResult.AlreadyUsed, AuthCodePolicy.Check(_hasher, stored, code, DateTime.UtcNow));
    }

    [Fact]
    public void RecordFailedAttempt_LocksAfterMaxAttempts()
    {
        var user = NewUser();
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < AuthCodePolicy.MaxAttempts; i++)
            AuthCodePolicy.RecordFailedAttempt(user, now);

        Assert.True(AuthCodePolicy.IsLocked(user, now.AddSeconds(1)));
        Assert.Equal(AuthCodePolicy.MaxAttempts, user.VerificationAttempts);
    }

    [Fact]
    public void EvaluateLock_ResetsCounterAndLock_WhenExpired()
    {
        var user = NewUser();
        var lockAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < AuthCodePolicy.MaxAttempts; i++)
            AuthCodePolicy.RecordFailedAttempt(user, lockAt);

        var afterLock = lockAt.Add(AuthCodePolicy.LockDuration).AddSeconds(1);
        AuthCodePolicy.ClearExpiredLock(user, afterLock);

        Assert.False(AuthCodePolicy.IsLocked(user, afterLock));
        Assert.Equal(0, user.VerificationAttempts);
        Assert.Null(user.LockedUntil);
    }

    [Fact]
    public void Create_SetsTypeHashAndExpiry_FromLifetime()
    {
        var userId = Guid.NewGuid();
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var code = AuthCodePolicy.Create(_hasher, userId, AuthCodeType.Reset, "123456", now);

        Assert.Equal(userId, code.UserId);
        Assert.Equal(AuthCodeType.Reset, code.Type);
        Assert.False(code.Used);
        Assert.Equal(now, code.CreatedAt);
        Assert.Equal(now.Add(AuthCodePolicy.ResetLifetime), code.ExpiresAt);
        Assert.True(AuthCodePolicy.CodeMatches(_hasher, code.CodeHash, "123456"));
    }

    [Fact]
    public void Burn_MarksCodeUsed()
    {
        var code = AuthCodePolicy.Create(_hasher, Guid.NewGuid(), AuthCodeType.Verify, "123456", DateTime.UtcNow);

        AuthCodePolicy.Burn(code);

        Assert.True(code.Used);
        Assert.Equal(CodeCheckResult.AlreadyUsed, AuthCodePolicy.Check(_hasher, code, "123456", DateTime.UtcNow));
    }

    [Fact]
    public void RecordFailedAttempt_DoesNotSlideLock_WhenAlreadyLocked()
    {
        var user = NewUser();
        var lockAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < AuthCodePolicy.MaxAttempts; i++)
            AuthCodePolicy.RecordFailedAttempt(user, lockAt);

        Assert.Equal(lockAt.Add(AuthCodePolicy.LockDuration), user.LockedUntil);

        AuthCodePolicy.RecordFailedAttempt(user, lockAt.AddMinutes(30));

        Assert.Equal(lockAt.Add(AuthCodePolicy.LockDuration), user.LockedUntil);
        Assert.Equal(AuthCodePolicy.MaxAttempts, user.VerificationAttempts);
    }

    [Fact]
    public void ResetAttempts_ClearsCounterAndLock()
    {
        var user = NewUser();
        AuthCodePolicy.RecordFailedAttempt(user, DateTime.UtcNow);

        AuthCodePolicy.ResetAttempts(user);

        Assert.Equal(0, user.VerificationAttempts);
        Assert.Null(user.LockedUntil);
    }

    [Fact]
    public void RecordChallengeAttempt_ClosesChallengeAtMaxAttempts()
    {
        var stored = NewCode(_hasher, "123456");

        for (var i = 0; i < AuthCodePolicy.MaxAttempts - 1; i++)
            Assert.False(AuthCodePolicy.RecordChallengeAttempt(stored, AuthCodePolicy.MaxAttempts));

        Assert.False(stored.Used);

        Assert.True(AuthCodePolicy.RecordChallengeAttempt(stored, AuthCodePolicy.MaxAttempts));
        Assert.True(stored.Used);
        Assert.Equal(AuthCodePolicy.MaxAttempts, stored.Attempts);
        Assert.True(AuthCodePolicy.IsClosedByAttempts(stored, AuthCodePolicy.MaxAttempts));
    }

    [Fact]
    public void IsClosedByAttempts_FalseForSuccessfullyConsumedCode()
    {
        var stored = NewCode(_hasher, "123456");
        AuthCodePolicy.RecordChallengeAttempt(stored, AuthCodePolicy.MaxAttempts);
        AuthCodePolicy.Burn(stored);

        Assert.False(AuthCodePolicy.IsClosedByAttempts(stored, AuthCodePolicy.MaxAttempts));
    }

    [Fact]
    public void RecordChallengeAttempt_ReturnsClosed_ForAlreadyUsedCode()
    {
        var stored = NewCode(_hasher, "123456");
        AuthCodePolicy.Burn(stored);

        Assert.True(AuthCodePolicy.RecordChallengeAttempt(stored, AuthCodePolicy.MaxAttempts));
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
            CodeHash = AuthCodePolicy.HashCode(hasher, code),
            CreatedAt = now,
            ExpiresAt = now.Add(AuthCodePolicy.LifetimeOf(AuthCodeType.Verify))
        };
    }

    private static User NewUser() => new()
    {
        Id = Guid.NewGuid(),
        Email = "user@example.com",
        PasswordHash = "hash"
    };
}
