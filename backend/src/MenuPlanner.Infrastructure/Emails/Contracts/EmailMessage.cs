namespace MenuPlanner.Infrastructure.Emails.Contracts;

/// <summary>Письмо для транспорта: получатель, отправитель, тема и HTML-тело.</summary>
public sealed record EmailMessage(
    string To,
    string From,
    string FromName,
    string Subject,
    string HtmlBody);
