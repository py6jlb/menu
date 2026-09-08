using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace MenuPlanner.Api.Emails;

public sealed class SmtpEmailTransport : IEmailTransport
{
    private readonly EmailOptions _options;

    public SmtpEmailTransport(EmailOptions options)
    {
        _options = options;
    }

    public async Task SendAsync(EmailMessage message)
    {
        using var client = new SmtpClient();
        var secure = _options.EnableStartTls
            ? SecureSocketOptions.StartTlsWhenAvailable
            : SecureSocketOptions.None;

        await client.ConnectAsync(_options.Host, _options.Port, secure);

        if (!string.IsNullOrEmpty(_options.User))
            await client.AuthenticateAsync(_options.User, _options.Password);

        await client.SendAsync(ToMimeMessage(message));
        await client.DisconnectAsync(true);
    }

    private MimeMessage ToMimeMessage(EmailMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromName, message.From));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody }.ToMessageBody();
        return mime;
    }
}
