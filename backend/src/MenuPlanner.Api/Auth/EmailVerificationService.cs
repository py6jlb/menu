using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MenuPlanner.Api.Auth.Codes;
using MenuPlanner.Api.Data;
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
/// в PostgreSQL, поэтому конкурентные verify/resend/unlock не теряют попытки,
/// не оставляют несколько действующих кодов и не возвращают утраченную
/// блокировку. На нереляционном провайдере (InMemory в быстрых тестах) шаг
/// блокировки пропускается.
/// </summary>
public sealed class EmailVerificationService
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<User> _hasher;
    private readonly AuthCodeOptions _options;
    private readonly IAuthCodeGenerator _generator;
    private readonly TimeProvider _clock;

    public EmailVerificationService(
        AppDbContext db,
        IPasswordHasher<User> hasher,
        AuthCodeOptions options,
        IAuthCodeGenerator generator,
        TimeProvider clock)
    {
        _db = db;
        _hasher = hasher;
        _options = options;
        _generator = generator;
        _clock = clock;
    }

    /// <summary>Первичная выдача кода при регистрации: без cooldown и проверок блокировки.</summary>
    public async Task<string> IssueInitialCodeAsync(User user, CancellationToken ct = default)
    {
        var now = Now;
        await using var tx = await BeginCriticalSectionAsync(user, ct);
        var code = await IssueAsync(user.Id, AuthCodeType.Verify, now, ct);
        await SaveAndCommitAsync(tx, ct);
        return code;
    }

    public async Task<ResendEmailResult> ResendAsync(User user, CancellationToken ct = default)
    {
        var now = Now;
        await using var tx = await BeginCriticalSectionAsync(user, ct);

        if (user.IsEmailVerified)
            return new ResendEmailResult(ResendEmailOutcome.AlreadyVerified, null, 0);

        AuthCodeService.ClearExpiredLock(user, now);
        if (AuthCodeService.IsLocked(user, now))
        {
            await SaveAndCommitAsync(tx, ct);
            return new ResendEmailResult(ResendEmailOutcome.Locked, null, 0);
        }

        var cooldown = TimeSpan.FromMinutes(_options.ResendCooldownMinutes);
        var latest = await LatestActiveCodeAsync(user.Id, AuthCodeType.Verify, ct);
        if (cooldown > TimeSpan.Zero && latest is not null)
        {
            var elapsed = now - latest.CreatedAt;
            if (elapsed < cooldown)
            {
                var left = (int)Math.Ceiling((cooldown - elapsed).TotalSeconds);
                await SaveAndCommitAsync(tx, ct);
                return new ResendEmailResult(ResendEmailOutcome.TooSoon, null, left);
            }
        }

        var code = await IssueAsync(user.Id, AuthCodeType.Verify, now, ct);
        await SaveAndCommitAsync(tx, ct);
        return new ResendEmailResult(ResendEmailOutcome.Sent, code, 0);
    }

    public async Task<VerifyEmailResult> VerifyAsync(User user, string code, CancellationToken ct = default)
    {
        var now = Now;
        await using var tx = await BeginCriticalSectionAsync(user, ct);

        if (user.IsEmailVerified)
            return new VerifyEmailResult(VerifyEmailOutcome.AlreadyVerified, null);

        AuthCodeService.ClearExpiredLock(user, now);
        if (AuthCodeService.IsLocked(user, now))
        {
            await SaveAndCommitAsync(tx, ct);
            return new VerifyEmailResult(VerifyEmailOutcome.Locked, null);
        }

        var stored = await LatestActiveCodeAsync(user.Id, AuthCodeType.Verify, ct);
        var outcome = stored is null
            ? VerifyEmailOutcome.InvalidCode
            : Map(AuthCodeService.Check(_hasher, stored, code, now));

        if (outcome == VerifyEmailOutcome.Verified)
        {
            AuthCodeService.Burn(stored!);
            user.IsEmailVerified = true;
            user.EmailVerifiedAt = now;
            AuthCodeService.ResetAttempts(user);
            await SaveAndCommitAsync(tx, ct);
            return new VerifyEmailResult(VerifyEmailOutcome.Verified, user);
        }

        AuthCodeService.RecordFailedAttempt(
            user, now, _options.MaxAttempts, TimeSpan.FromDays(_options.LockDurationDays));

        var locked = AuthCodeService.IsLocked(user, now);
        await SaveAndCommitAsync(tx, ct);
        return new VerifyEmailResult(locked ? VerifyEmailOutcome.Locked : outcome, null);
    }

    /// <summary>Административная разблокировка: сбрасывает попытки и блокировку тех же данных.</summary>
    public async Task UnlockAsync(User user, CancellationToken ct = default)
    {
        await using var tx = await BeginCriticalSectionAsync(user, ct);
        AuthCodeService.ResetAttempts(user);
        await SaveAndCommitAsync(tx, ct);
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    private static VerifyEmailOutcome Map(CodeCheckResult result) => result switch
    {
        CodeCheckResult.Ok => VerifyEmailOutcome.Verified,
        CodeCheckResult.Expired => VerifyEmailOutcome.ExpiredCode,
        CodeCheckResult.AlreadyUsed => VerifyEmailOutcome.CodeAlreadyUsed,
        _ => VerifyEmailOutcome.InvalidCode
    };

    private async Task<string> IssueAsync(Guid userId, AuthCodeType type, DateTime now, CancellationToken ct)
    {
        // Новый действующий код инвалидирует все прежние той же операции.
        var unused = await _db.AuthCodes
            .Where(c => c.UserId == userId && c.Type == type && !c.Used)
            .ToListAsync(ct);
        foreach (var stored in unused)
            AuthCodeService.Burn(stored);

        var code = _generator.Generate();
        _db.AuthCodes.Add(AuthCodeService.Create(_hasher, userId, type, code, now));
        return code;
    }

    private Task<AuthCode?> LatestActiveCodeAsync(Guid userId, AuthCodeType type, CancellationToken ct) =>
        _db.AuthCodes
            .Where(c => c.UserId == userId && c.Type == type)
            .OrderByDescending(c => c.CreatedAt)
            .ThenBy(c => c.Used)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Открывает критическую секцию пользователя: транзакцию с блокировкой его
    /// строки. На реляционном провайдере строка перечитывается после блокировки,
    /// чтобы решение принималось по актуальному состоянию.
    /// </summary>
    private async Task<IDbContextTransaction?> BeginCriticalSectionAsync(User user, CancellationToken ct)
    {
        if (!_db.Database.IsRelational())
            return null;

        var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.Database.ExecuteSqlRawAsync(
            "SELECT 1 FROM \"Users\" WHERE \"Id\" = {0} FOR UPDATE",
            new object[] { user.Id }, ct);
        await _db.Entry(user).ReloadAsync(ct);
        return tx;
    }

    private async Task SaveAndCommitAsync(IDbContextTransaction? tx, CancellationToken ct)
    {
        await _db.SaveChangesAsync(ct);
        if (tx is not null)
            await tx.CommitAsync(ct);
    }
}
