using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Emails.Outbox;

/// <summary>
/// Постановка письма в очередь. Запись добавляется в уже открытую транзакцию
/// вызывающего (аккаунт/challenge и принятая доставка фиксируются вместе) и не
/// коммитится самостоятельно. Содержимое шифруется при постановке.
/// </summary>
public sealed class EmailOutbox
{
    private readonly AppDbContext _db;
    private readonly IOutboxPayloadProtector _protector;
    private readonly TimeProvider _clock;

    public EmailOutbox(AppDbContext db, IOutboxPayloadProtector protector, TimeProvider clock)
    {
        _db = db;
        _protector = protector;
        _clock = clock;
    }

    public Task EnqueueAsync(User user, AuthCode challenge, string plaintextCode, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        _db.EmailOutboxMessages.Add(new EmailOutboxMessage
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            AuthCodeId = challenge.Id,
            Type = challenge.Type,
            Recipient = user.Email,
            ProtectedPayload = _protector.Protect(plaintextCode),
            Status = EmailOutboxStatus.Pending,
            CreatedAt = now,
            NextAttemptAt = now
        });
        return Task.CompletedTask;
    }
}
