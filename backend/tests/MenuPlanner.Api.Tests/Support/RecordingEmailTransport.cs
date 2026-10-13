using MenuPlanner.Api.Emails.Contracts;

namespace MenuPlanner.Api.Tests.Support;

/// <summary>Транспорт, запоминающий отправленные письма: без сети и внешних зависимостей.</summary>
internal sealed class RecordingEmailTransport : IEmailTransport
{
    private readonly object _gate = new();
    private readonly List<EmailMessage> _emails = new();

    public IReadOnlyList<EmailMessage> Emails
    {
        get
        {
            lock (_gate)
                return _emails.ToList();
        }
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            _emails.Add(message);
        return Task.CompletedTask;
    }
}
