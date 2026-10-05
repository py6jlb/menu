using System.Net.Sockets;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace MenuPlanner.Api.Emails;

/// <summary>
/// Отправка через SMTP с обязательной защитой: либо явно требуемый STARTTLS,
/// либо неявный TLS. Даунгрейд «когда доступно» и отключение проверки
/// сертификата невозможны. Сбой заворачивается в <see cref="EmailDeliveryException"/>
/// без учётных данных и тела письма.
/// </summary>
public sealed class SmtpEmailTransport : IEmailTransport
{
    private readonly EmailOptions _options;
    private readonly Func<SmtpClient> _clientFactory;

    public SmtpEmailTransport(EmailOptions options)
        : this(options, static () => new SmtpClient())
    {
    }

    internal SmtpEmailTransport(EmailOptions options, Func<SmtpClient> clientFactory)
    {
        _options = options;
        _clientFactory = clientFactory;
    }

    public static SecureSocketOptions ResolveSocketOptions(SmtpSecurity security) => security switch
    {
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.Ssl => SecureSocketOptions.SslOnConnect,
        _ => throw new ArgumentOutOfRangeException(nameof(security), security, "Неизвестный режим TLS")
    };

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        using var client = _clientFactory();
        client.Timeout = checked(_options.TimeoutSeconds * 1000);

        try
        {
            await client.ConnectAsync(
                _options.Host, _options.Port, ResolveSocketOptions(_options.Security), cancellationToken);

            if (!string.IsNullOrEmpty(_options.User))
                await client.AuthenticateAsync(_options.User, _options.Password, cancellationToken);

            await client.SendAsync(ToMimeMessage(message), cancellationToken);

            // Письмо принято сервером; сбой вежливого QUIT не превращает принятие в ошибку.
            try
            {
                await client.DisconnectAsync(quit: true, CancellationToken.None);
            }
            catch
            {
            }
        }
        catch (SslHandshakeException ex)
        {
            throw Failure(EmailFailureReason.Certificate, ex);
        }
        catch (AuthenticationException ex)
        {
            throw Failure(EmailFailureReason.Authentication, ex);
        }
        catch (NotSupportedException ex)
        {
            throw Failure(EmailFailureReason.TlsUnavailable, ex);
        }
        catch (SmtpCommandException ex)
        {
            throw Failure(EmailFailureReason.Protocol, ex);
        }
        catch (SmtpProtocolException ex)
        {
            throw Failure(EmailFailureReason.Protocol, ex);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            throw Failure(EmailFailureReason.Timeout, ex);
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            throw Failure(EmailFailureReason.Connection, ex);
        }
        catch (Exception ex)
        {
            throw Failure(EmailFailureReason.Unknown, ex);
        }
    }

    private static EmailDeliveryException Failure(EmailFailureReason reason, Exception source) =>
        new(reason, source.GetType().Name);

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
