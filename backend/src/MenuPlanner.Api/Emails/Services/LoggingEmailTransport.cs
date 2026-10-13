using Microsoft.Extensions.Logging;

namespace MenuPlanner.Api.Emails;

/// <summary>
/// Dev/test-транспорт: полное письмо (включая код и HTML) пишется в журнал.
/// Выбирается только явно (<c>EMAIL_TRANSPORT=log</c> или lab-режим с пустым
/// SMTP); в production недоступен.
/// </summary>
public sealed class LoggingEmailTransport : IEmailTransport
{
    private readonly ILogger<LoggingEmailTransport> _logger;

    public LoggingEmailTransport(ILogger<LoggingEmailTransport> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Email] SMTP не настроен — письмо записано в лог.\nКому: {To}\nОт: {From} ({FromName})\nТема: {Subject}\n\n{HtmlBody}",
            message.To, message.From, message.FromName, message.Subject, message.HtmlBody);
        return Task.CompletedTask;
    }
}
