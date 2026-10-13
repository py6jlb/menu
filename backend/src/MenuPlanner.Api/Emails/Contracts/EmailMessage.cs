namespace MenuPlanner.Api.Emails.Contracts;

public sealed record EmailMessage(
    string To,
    string From,
    string FromName,
    string Subject,
    string HtmlBody);
