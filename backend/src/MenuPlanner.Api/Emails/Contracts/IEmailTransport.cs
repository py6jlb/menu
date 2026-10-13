namespace MenuPlanner.Api.Emails.Contracts;

public interface IEmailTransport
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
