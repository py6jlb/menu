using Microsoft.Extensions.Logging;

namespace MenuPlanner.Api.Emails;

public sealed class LoggingEmailTransport : IEmailTransport
{
    private readonly ILogger<LoggingEmailTransport> _logger;

    public LoggingEmailTransport(ILogger<LoggingEmailTransport> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(EmailMessage message)
    {
        _logger.LogInformation(
            "[Email] SMTP не настроен — письмо записано в лог.\nКому: {To}\nОт: {From} ({FromName})\nТема: {Subject}\n\n{HtmlBody}",
            message.To, message.From, message.FromName, message.Subject, message.HtmlBody);
        return Task.CompletedTask;
    }
}
