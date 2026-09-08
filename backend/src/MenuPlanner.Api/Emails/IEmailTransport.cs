namespace MenuPlanner.Api.Emails;

public interface IEmailTransport
{
    Task SendAsync(EmailMessage message);
}
