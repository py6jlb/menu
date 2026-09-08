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

    public Task SendVerificationCodeAsync(string to, string code) =>
        SendAsync(to, EmailTemplates.Verification(code));

    public Task SendPasswordResetCodeAsync(string to, string code) =>
        SendAsync(to, EmailTemplates.PasswordReset(code));

    private Task SendAsync(string to, (string Subject, string HtmlBody) letter) =>
        _transport.SendAsync(new EmailMessage(
            to,
            _options.From,
            _options.FromName,
            letter.Subject,
            letter.HtmlBody));
}
