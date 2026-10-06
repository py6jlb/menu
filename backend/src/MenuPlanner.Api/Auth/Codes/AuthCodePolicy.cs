using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Auth.Codes;

public enum CodeCheckResult
{
    Ok,
    Expired,
    Invalid,
    AlreadyUsed
}

public static class AuthCodePolicy
{
    public const int CodeLength = 6;
    public const int MaxAttempts = 5;
    public static readonly TimeSpan VerifyLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan LockDuration = TimeSpan.FromDays(3);

    public static TimeSpan LifetimeOf(AuthCodeType type) =>
        type == AuthCodeType.Verify ? VerifyLifetime : ResetLifetime;

    public static AuthCode Create(
        IPasswordHasher<User> hasher,
        Guid userId,
        AuthCodeType type,
        string code,
        DateTime now) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = type,
            CodeHash = HashCode(hasher, code),
            CreatedAt = now,
            ExpiresAt = now.Add(LifetimeOf(type)),
            Used = false
        };

    public static void Burn(AuthCode stored) => stored.Used = true;

    public static string GenerateCode()
    {
        var code = new char[CodeLength];
        for (var i = 0; i < CodeLength; i++)
            code[i] = (char)('0' + RandomNumberGenerator.GetInt32(10));
        return new string(code);
    }

    public static string HashCode(IPasswordHasher<User> hasher, string code) =>
        hasher.HashPassword(null!, code);

    public static bool CodeMatches(IPasswordHasher<User> hasher, string codeHash, string code) =>
        hasher.VerifyHashedPassword(null!, codeHash, code) != PasswordVerificationResult.Failed;

    public static CodeCheckResult Check(
        IPasswordHasher<User> hasher,
        AuthCode stored,
        string code,
        DateTime now)
    {
        if (stored.Used)
            return CodeCheckResult.AlreadyUsed;
        if (stored.ExpiresAt <= now)
            return CodeCheckResult.Expired;
        if (!CodeMatches(hasher, stored.CodeHash, code))
            return CodeCheckResult.Invalid;
        return CodeCheckResult.Ok;
    }

    public static bool IsLocked(User user, DateTime now) =>
        user.LockedUntil is { } until && until > now;

    public static void ClearExpiredLock(User user, DateTime now)
    {
        if (user.LockedUntil is { } until && until <= now)
        {
            user.LockedUntil = null;
            user.VerificationAttempts = 0;
        }
    }

    public static void RecordFailedAttempt(User user, DateTime now, int maxAttempts = MaxAttempts, TimeSpan? lockDuration = null)
    {
        if (IsLocked(user, now))
            return;

        user.VerificationAttempts++;
        if (user.VerificationAttempts >= maxAttempts)
            user.LockedUntil = now.Add(lockDuration ?? LockDuration);
    }

    public static void ResetAttempts(User user)
    {
        user.VerificationAttempts = 0;
        user.LockedUntil = null;
    }

    /// <summary>
    /// Учитывает неверную попытку в рамках конкретного challenge. Счётчик живёт
    /// на коде, а не на пользователе: по достижении лимита закрывается именно
    /// этот код, аккаунт не блокируется. Возвращает <c>true</c>, если challenge
    /// закрыт (в том числе если был закрыт/потреблён ранее).
    /// </summary>
    public static bool RecordChallengeAttempt(AuthCode stored, int maxAttempts)
    {
        if (stored.Used)
            return true;

        stored.Attempts++;
        if (stored.Attempts >= maxAttempts)
        {
            Burn(stored);
            return true;
        }

        return false;
    }

    /// <summary>Challenge закрыт исчерпанием попыток (а не успешным потреблением).</summary>
    public static bool IsClosedByAttempts(AuthCode stored, int maxAttempts) =>
        stored.Used && stored.Attempts >= maxAttempts;
}
