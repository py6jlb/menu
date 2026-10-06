using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Emails.Outbox;

public enum OutboxDeliveryOutcome
{
    Delivered,
    Rescheduled,
    Abandoned
}

public readonly record struct OutboxDispatchResult(int Claimed, int Delivered, int Rescheduled, int Abandoned);

/// <summary>
/// Отправка писем из очереди. Захват строк атомарен: на реляционном провайдере
/// строки помечаются <c>FOR UPDATE SKIP LOCKED</c>, поэтому параллельные воркеры
/// не берут одну запись. Перед отправкой проверяются срок действия и
/// актуальность challenge: истёкший, потреблённый или заменённый код не
/// отправляется как действующий. Повтор при неопределённом результате SMTP
/// относится к тому же challenge и не создаёт новый код.
/// </summary>
public sealed class EmailOutboxProcessor
{
    private readonly AppDbContext _db;
    private readonly EmailSender _sender;
    private readonly IOutboxPayloadProtector _protector;
    private readonly EmailOutboxOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<EmailOutboxProcessor> _logger;

    public EmailOutboxProcessor(
        AppDbContext db,
        EmailSender sender,
        IOutboxPayloadProtector protector,
        EmailOutboxOptions options,
        TimeProvider clock,
        ILogger<EmailOutboxProcessor> logger)
    {
        _db = db;
        _sender = sender;
        _protector = protector;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    public async Task<OutboxDispatchResult> DispatchDueAsync(CancellationToken ct = default)
    {
        var now = Now;
        await PurgeExpiredAsync(now, ct);

        var token = Guid.NewGuid();
        var claimed = await ClaimAsync(token, now, ct);
        if (claimed == 0)
            return new OutboxDispatchResult(0, 0, 0, 0);

        // Обрабатываем только что захваченные строки отдельными экземплярами:
        // не полагаемся на состояние отслеживания контекста.
        _db.ChangeTracker.Clear();

        var messages = await _db.EmailOutboxMessages
            .AsNoTracking()
            .Where(m => m.ClaimToken == token)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        int delivered = 0, rescheduled = 0, abandoned = 0;
        foreach (var message in messages)
        {
            switch (await DeliverAsync(message, ct))
            {
                case OutboxDeliveryOutcome.Delivered: delivered++; break;
                case OutboxDeliveryOutcome.Rescheduled: rescheduled++; break;
                default: abandoned++; break;
            }
        }

        return new OutboxDispatchResult(claimed, delivered, rescheduled, abandoned);
    }

    private async Task<int> ClaimAsync(Guid token, DateTime now, CancellationToken ct)
    {
        if (_db.SupportsRelationalLocking())
        {
            var leaseCutoff = now - TimeSpan.FromSeconds(_options.LeaseSeconds);
            return await _db.Database.ExecuteSqlRawAsync(
                """
                UPDATE "EmailOutboxMessages"
                SET "Status" = 'InProgress', "ClaimToken" = {0}, "ClaimedAt" = {1}
                WHERE "Id" IN (
                    SELECT "Id" FROM "EmailOutboxMessages"
                    WHERE ("Status" = 'Pending' AND "NextAttemptAt" <= {1})
                       OR ("Status" = 'InProgress' AND "ClaimedAt" IS NOT NULL AND "ClaimedAt" < {2})
                    ORDER BY "CreatedAt"
                    FOR UPDATE SKIP LOCKED
                    LIMIT {3}
                )
                """,
                new object[] { token, now, leaseCutoff, _options.BatchSize },
                ct);
        }

        var lease = now - TimeSpan.FromSeconds(_options.LeaseSeconds);
        var candidates = await _db.EmailOutboxMessages
            .Where(m => (m.Status == EmailOutboxStatus.Pending && m.NextAttemptAt <= now)
                || (m.Status == EmailOutboxStatus.InProgress
                    && m.ClaimedAt != null && m.ClaimedAt < lease))
            .OrderBy(m => m.CreatedAt)
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        foreach (var message in candidates)
        {
            message.Status = EmailOutboxStatus.InProgress;
            message.ClaimToken = token;
            message.ClaimedAt = now;
        }

        if (candidates.Count > 0)
            await _db.SaveChangesAsync(ct);
        return candidates.Count;
    }

    private async Task<OutboxDeliveryOutcome> DeliverAsync(EmailOutboxMessage message, CancellationToken ct)
    {
        var now = Now;
        var challenge = await _db.AuthCodes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == message.AuthCodeId, ct);
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == message.UserId, ct);

        var reason = SkipReason(message, challenge, user, now);
        if (reason is not null)
            return await AbandonAsync(message, reason, ct);

        if (!_protector.TryUnprotect(message.ProtectedPayload, out var code) || string.IsNullOrEmpty(code))
            return await AbandonAsync(message, "payload", ct);

        try
        {
            switch (message.Type)
            {
                case AuthCodeType.Verify:
                    await _sender.SendVerificationCodeAsync(message.Recipient, code, ct);
                    break;
                case AuthCodeType.Reset:
                    await _sender.SendPasswordResetCodeAsync(message.Recipient, code, ct);
                    break;
                default:
                    return await AbandonAsync(message, "type", ct);
            }
        }
        catch (EmailDeliveryException failure)
        {
            // Письмо не потеряно: та же запись и тот же challenge будут повторены
            // с задержкой. Причина логируется обобщённо, без кода и адреса.
            EmailDeliveryFailure.LogSafe(_logger, failure);
            return await RescheduleOrFailAsync(message, failure.Reason.ToString(), now, ct);
        }

        _db.EmailOutboxMessages.Remove(message);
        await _db.SaveChangesAsync(ct);
        return OutboxDeliveryOutcome.Delivered;
    }

    private static string? SkipReason(
        EmailOutboxMessage message, AuthCode? challenge, User? user, DateTime now)
    {
        if (challenge is null || user is null) return "missing";
        if (challenge.Used) return "used";
        if (challenge.ExpiresAt <= now) return "expired";
        if (message.Type == AuthCodeType.Verify && user.IsEmailVerified) return "verified";
        return null;
    }

    private async Task<OutboxDeliveryOutcome> RescheduleOrFailAsync(
        EmailOutboxMessage message, string reason, DateTime now, CancellationToken ct)
    {
        message.Attempts += 1;
        message.ClaimToken = null;
        message.ClaimedAt = null;
        message.LastFailureReason = reason;

        if (message.Attempts >= _options.MaxAttempts)
        {
            message.Status = EmailOutboxStatus.Failed;
            _db.EmailOutboxMessages.Update(message);
            await _db.SaveChangesAsync(ct);
            return OutboxDeliveryOutcome.Abandoned;
        }

        message.Status = EmailOutboxStatus.Pending;
        message.NextAttemptAt = now + _options.DelayBefore(message.Attempts);
        _db.EmailOutboxMessages.Update(message);
        await _db.SaveChangesAsync(ct);
        return OutboxDeliveryOutcome.Rescheduled;
    }

    private async Task<OutboxDeliveryOutcome> AbandonAsync(
        EmailOutboxMessage message, string reason, CancellationToken ct)
    {
        // Потерявший актуальность challenge завершён: письмо не отправляется и
        // запись с защищённым содержимым удаляется сразу.
        _logger.LogInformation(
            "Письмо из очереди снято без отправки: {Reason}", reason);
        _db.EmailOutboxMessages.Remove(message);
        await _db.SaveChangesAsync(ct);
        return OutboxDeliveryOutcome.Abandoned;
    }

    private async Task PurgeExpiredAsync(DateTime now, CancellationToken ct)
    {
        var cutoff = now - TimeSpan.FromDays(_options.RetentionDays);
        var stale = await _db.EmailOutboxMessages
            .Where(m => m.Status == EmailOutboxStatus.Failed && m.CreatedAt < cutoff)
            .Take(_options.BatchSize * 10)
            .ToListAsync(ct);

        if (stale.Count == 0)
            return;

        _db.EmailOutboxMessages.RemoveRange(stale);
        await _db.SaveChangesAsync(ct);
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;
}
