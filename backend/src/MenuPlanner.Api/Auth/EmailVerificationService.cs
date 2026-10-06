using Microsoft.AspNetCore.Identity;
using MenuPlanner.Api.Auth.Codes;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Auth;

public enum VerifyEmailOutcome
{
    Verified,
    AlreadyVerified,
    InvalidCode,
    ExpiredCode,
    CodeAlreadyUsed,
    Locked
}

public readonly record struct VerifyEmailResult(VerifyEmailOutcome Outcome, User? User);

public enum ResendEmailOutcome
{
    Sent,
    AlreadyVerified,
    TooSoon,
    Locked
}

public readonly record struct ResendEmailResult(ResendEmailOutcome Outcome, string? Code, int RetryAfterSeconds);

/// <summary>
/// Предметный модуль подтверждения почты: владеет последовательностью инвариантов
/// выдачи, cooldown, срока действия, попыток, блокировки и потребления кода.
/// Endpoint только транслирует результат в HTTP.
///
/// Все операции над одним пользователем сериализуются блокировкой его строки
/// в PostgreSQL (через <see cref="AuthCodeLifecycle"/>), поэтому конкурентные
/// verify/resend/unlock не теряют попытки, не оставляют несколько действующих
/// кодов и не возвращают утраченную блокировку.
/// </summary>
public sealed class EmailVerificationService
{
    private readonly AuthCodeLifecycle _codes;
    private readonly IPasswordHasher<User> _hasher;
    private readonly AuthCodeOptions _options;
    private readonly TimeProvider _clock;

    public EmailVerificationService(
        AuthCodeLifecycle codes,
        IPasswordHasher<User> hasher,
        AuthCodeOptions options,
        TimeProvider clock)
    {
        _codes = codes;
        _hasher = hasher;
        _options = options;
        _clock = clock;
    }

    /// <summary>Первичная выдача кода при регистрации: без cooldown и проверок блокировки.</summary>
    public async Task<string> IssueInitialCodeAsync(User user, CancellationToken ct = default)
    {
        var now = Now;
        await using var tx = await _codes.BeginCriticalSectionAsync(user, ct);
        var code = await _codes.IssueAsync(user.Id, AuthCodeType.Verify, now, ct);
        await _codes.SaveAndCommitAsync(tx, ct);
        return code;
    }

    public async Task<ResendEmailResult> ResendAsync(User user, CancellationToken ct = default)
    {
        var now = Now;
        await using var tx = await _codes.BeginCriticalSectionAsync(user, ct);

        if (user.IsEmailVerified)
            return new ResendEmailResult(ResendEmailOutcome.AlreadyVerified, null, 0);

        AuthCodePolicy.ClearExpiredLock(user, now);
        if (AuthCodePolicy.IsLocked(user, now))
        {
            await _codes.SaveAndCommitAsync(tx, ct);
            return new ResendEmailResult(ResendEmailOutcome.Locked, null, 0);
        }

        var cooldown = TimeSpan.FromMinutes(_options.ResendCooldownMinutes);
        var latest = await _codes.LatestAsync(user.Id, AuthCodeType.Verify, ct);
        if (cooldown > TimeSpan.Zero && latest is not null)
        {
            var elapsed = now - latest.CreatedAt;
            if (elapsed < cooldown)
            {
                var left = (int)Math.Ceiling((cooldown - elapsed).TotalSeconds);
                await _codes.SaveAndCommitAsync(tx, ct);
                return new ResendEmailResult(ResendEmailOutcome.TooSoon, null, left);
            }
        }

        var code = await _codes.IssueAsync(user.Id, AuthCodeType.Verify, now, ct);
        await _codes.SaveAndCommitAsync(tx, ct);
        return new ResendEmailResult(ResendEmailOutcome.Sent, code, 0);
    }

    public async Task<VerifyEmailResult> VerifyAsync(User user, string code, CancellationToken ct = default)
    {
        var now = Now;
        await using var tx = await _codes.BeginCriticalSectionAsync(user, ct);

        if (user.IsEmailVerified)
            return new VerifyEmailResult(VerifyEmailOutcome.AlreadyVerified, null);

        AuthCodePolicy.ClearExpiredLock(user, now);
        if (AuthCodePolicy.IsLocked(user, now))
        {
            await _codes.SaveAndCommitAsync(tx, ct);
            return new VerifyEmailResult(VerifyEmailOutcome.Locked, null);
        }

        var stored = await _codes.LatestAsync(user.Id, AuthCodeType.Verify, ct);
        var outcome = stored is null
            ? VerifyEmailOutcome.InvalidCode
            : Map(AuthCodePolicy.Check(_hasher, stored, code, now));

        if (outcome == VerifyEmailOutcome.Verified)
        {
            AuthCodePolicy.Burn(stored!);
            user.IsEmailVerified = true;
            user.EmailVerifiedAt = now;
            AuthCodePolicy.ResetAttempts(user);
            await _codes.SaveAndCommitAsync(tx, ct);
            return new VerifyEmailResult(VerifyEmailOutcome.Verified, user);
        }

        AuthCodePolicy.RecordFailedAttempt(
            user, now, _options.MaxAttempts, TimeSpan.FromDays(_options.LockDurationDays));

        var locked = AuthCodePolicy.IsLocked(user, now);
        await _codes.SaveAndCommitAsync(tx, ct);
        return new VerifyEmailResult(locked ? VerifyEmailOutcome.Locked : outcome, null);
    }

    /// <summary>Административная разблокировка: сбрасывает попытки и блокировку тех же данных.</summary>
    public async Task UnlockAsync(User user, CancellationToken ct = default)
    {
        await using var tx = await _codes.BeginCriticalSectionAsync(user, ct);
        AuthCodePolicy.ResetAttempts(user);
        await _codes.SaveAndCommitAsync(tx, ct);
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    private static VerifyEmailOutcome Map(CodeCheckResult result) => result switch
    {
        CodeCheckResult.Ok => VerifyEmailOutcome.Verified,
        CodeCheckResult.Expired => VerifyEmailOutcome.ExpiredCode,
        CodeCheckResult.AlreadyUsed => VerifyEmailOutcome.CodeAlreadyUsed,
        _ => VerifyEmailOutcome.InvalidCode
    };
}
