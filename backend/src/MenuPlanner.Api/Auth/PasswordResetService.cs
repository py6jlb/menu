using Microsoft.AspNetCore.Identity;
using MenuPlanner.Api.Auth.Codes;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Emails.Outbox;

namespace MenuPlanner.Api.Auth;

public enum PasswordResetRequestOutcome
{
    Sent,
    TooSoon,
    NotEligible
}

public readonly record struct PasswordResetRequestResult(
    PasswordResetRequestOutcome Outcome, string? Code, int RetryAfterSeconds);

public enum PasswordResetOutcome
{
    Reset,
    InvalidCode,
    ExpiredCode,
    CodeAlreadyUsed,
    ChallengeClosed
}

public readonly record struct PasswordResetResult(PasswordResetOutcome Outcome, User? User);

/// <summary>
/// Предметный модуль восстановления пароля: владеет выдачей reset-кода (cooldown,
/// срок действия, единственный действующий challenge) и одной защищённой
/// операцией «потребить код + записать новый хэш пароля + увеличить версию
/// токенов». Endpoint только транслирует результат в HTTP.
///
/// Отличие от подтверждения почты: reset — анонимная операция, поэтому неверные
/// попытки считаются на конкретном challenge (<see cref="AuthCode.Attempts"/>),
/// а при исчерпании лимита закрывается только этот код. Аккаунт не блокируется,
/// и посторонний, знающий email, не может навязать длительный lockout запросами
/// без действующего кода. Атомарность обеспечивает <see cref="AuthCodeLifecycle"/>.
/// </summary>
public sealed class PasswordResetService
{
    private readonly AuthCodeLifecycle _codes;
    private readonly IPasswordHasher<User> _hasher;
    private readonly AuthCodeOptions _options;
    private readonly TimeProvider _clock;
    private readonly EmailOutbox _outbox;

    public PasswordResetService(
        AuthCodeLifecycle codes,
        IPasswordHasher<User> hasher,
        AuthCodeOptions options,
        TimeProvider clock,
        EmailOutbox outbox)
    {
        _codes = codes;
        _hasher = hasher;
        _options = options;
        _clock = clock;
        _outbox = outbox;
    }

    /// <summary>
    /// Запрашивает выдачу кода сброса. Нейтральность ответа обеспечивает endpoint;
    /// модуль лишь сообщает, отправили ли код, и остаток cooldown.
    /// </summary>
    public async Task<PasswordResetRequestResult> RequestAsync(
        User user, CancellationToken ct = default)
    {
        var now = Now;
        await using var tx = await _codes.BeginCriticalSectionAsync(user, ct);

        if (!user.IsEmailVerified)
        {
            await _codes.SaveAndCommitAsync(tx, ct);
            return new PasswordResetRequestResult(PasswordResetRequestOutcome.NotEligible, null, 0);
        }

        var latest = await _codes.LatestAsync(user.Id, AuthCodeType.Reset, ct);
        var left = AuthCodePolicy.CooldownSecondsLeft(
            latest, now, TimeSpan.FromMinutes(_options.ResendCooldownMinutes));
        if (left is { } seconds)
        {
            await _codes.SaveAndCommitAsync(tx, ct);
            return new PasswordResetRequestResult(PasswordResetRequestOutcome.TooSoon, null, seconds);
        }

        var issued = await _codes.IssueAsync(user.Id, AuthCodeType.Reset, now, ct);
        await _outbox.EnqueueAsync(user, issued.Challenge, issued.Plaintext, ct);
        await _codes.SaveAndCommitAsync(tx, ct);
        return new PasswordResetRequestResult(PasswordResetRequestOutcome.Sent, issued.Plaintext, 0);
    }

    /// <summary>
    /// Потребляет действующий reset-код и меняет пароль одной операцией:
    /// код помечается использованным, записывается новый хэш, инкрементируется
    /// <see cref="User.TokenVersion"/>. Неверная попытка относится только к
    /// действующему challenge и никогда не блокирует аккаунт.
    /// </summary>
    public async Task<PasswordResetResult> ResetAsync(
        User user, string code, string newPassword, CancellationToken ct = default)
    {
        var now = Now;
        await using var tx = await _codes.BeginCriticalSectionAsync(user, ct);

        var stored = await _codes.LatestAsync(user.Id, AuthCodeType.Reset, ct);
        if (stored is null)
        {
            await _codes.SaveAndCommitAsync(tx, ct);
            return new PasswordResetResult(PasswordResetOutcome.InvalidCode, null);
        }

        if (AuthCodePolicy.IsClosedByAttempts(stored, _options.MaxAttempts))
        {
            await _codes.SaveAndCommitAsync(tx, ct);
            return new PasswordResetResult(PasswordResetOutcome.ChallengeClosed, null);
        }

        var check = AuthCodePolicy.Check(_hasher, stored, code, now);
        if (check == CodeCheckResult.Ok)
        {
            AuthCodePolicy.Burn(stored);
            user.PasswordHash = _hasher.HashPassword(user, newPassword);
            user.TokenVersion++;
            await _codes.SaveAndCommitAsync(tx, ct);
            return new PasswordResetResult(PasswordResetOutcome.Reset, user);
        }

        if (check == CodeCheckResult.Invalid)
        {
            AuthCodePolicy.RecordChallengeAttempt(stored, _options.MaxAttempts);
            var outcome = AuthCodePolicy.IsClosedByAttempts(stored, _options.MaxAttempts)
                ? PasswordResetOutcome.ChallengeClosed
                : PasswordResetOutcome.InvalidCode;
            await _codes.SaveAndCommitAsync(tx, ct);
            return new PasswordResetResult(outcome, null);
        }

        // Истёкший или уже потреблённый код не является действующим challenge:
        // попытка не засчитывается и аккаунт не блокируется.
        await _codes.SaveAndCommitAsync(tx, ct);
        return new PasswordResetResult(
            check == CodeCheckResult.Expired
                ? PasswordResetOutcome.ExpiredCode
                : PasswordResetOutcome.CodeAlreadyUsed,
            null);
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;
}
