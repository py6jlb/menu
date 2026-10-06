using MenuPlanner.Api.Emails;
using MenuPlanner.Api.Emails.Outbox;

namespace MenuPlanner.Api.Tests;

/// <summary>Транспорт с управляемым сбоем: без сети и без ожиданий.</summary>
internal sealed class SwitchableEmailTransport : IEmailTransport
{
    private readonly object _gate = new();
    private readonly List<EmailMessage> _sent = new();

    /// <summary>Вернуть исключение, чтобы провалить отправку, либо <c>null</c> для успеха.</summary>
    public Func<EmailMessage, Exception?>? FailureFactory { get; set; }

    public IReadOnlyList<EmailMessage> Sent
    {
        get
        {
            lock (_gate)
                return _sent.ToList();
        }
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var failure = FailureFactory?.Invoke(message);
        if (failure is not null)
            throw failure;

        lock (_gate)
            _sent.Add(message);
        return Task.CompletedTask;
    }

    public static EmailDeliveryException Transient() =>
        new(EmailFailureReason.Connection, "SocketException");
}

/// <summary>Защита очереди, всегда падающая при постановке: проверка атомарности.</summary>
internal sealed class ThrowingOutboxProtector : IOutboxPayloadProtector
{
    public string Protect(string plaintext) =>
        throw new InvalidOperationException("Не удалось защитить содержимое письма.");

    public bool TryUnprotect(string protectedPayload, out string plaintext)
    {
        plaintext = "";
        return false;
    }
}
