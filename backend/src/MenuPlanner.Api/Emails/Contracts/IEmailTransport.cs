namespace MenuPlanner.Api.Emails.Contracts;

/// <summary>Шов доставки готового письма: продакшн — защищённый SMTP, dev/test — запись в лог.</summary>
public interface IEmailTransport
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
