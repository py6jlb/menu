namespace MenuPlanner.Api.Emails;

public sealed class EmailSender
{
    private readonly EmailOptions _options;
    private readonly IEmailTransport _transport;

    public EmailSender(EmailOptions options, IEmailTransport transport)
    {
        _options = options;
        _transport = transport;
    }

    public Task SendVerificationCodeAsync(
        string to, string code, CancellationToken cancellationToken = default) =>
        SendAsync(to, EmailTemplates.Verification(code), cancellationToken);

    public Task SendPasswordResetCodeAsync(
        string to, string code, CancellationToken cancellationToken = default) =>
        SendAsync(to, EmailTemplates.PasswordReset(code), cancellationToken);

    private Task SendAsync(
        string to, (string Subject, string HtmlBody) letter, CancellationToken cancellationToken) =>
        _transport.SendAsync(new EmailMessage(
            to,
            _options.From,
            _options.FromName,
            letter.Subject,
            letter.HtmlBody), cancellationToken);
}
