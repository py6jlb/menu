using System.Diagnostics;
using MailKit.Net.Smtp;
using MailKit.Security;
using Xunit;
using MenuPlanner.Api.Emails;

namespace MenuPlanner.Api.Tests;

public sealed class SmtpEmailTransportTests
{
    private static EmailOptions Options(int port) => new()
    {
        Host = "127.0.0.1",
        Port = port,
        From = "noreply@menu.local",
        FromName = "Меню для домохозяек",
        Security = SmtpSecurity.StartTls,
        TimeoutSeconds = 5
    };

    private static EmailMessage Message() => new(
        "user@example.com", "noreply@menu.local", "Меню для домохозяек",
        "Код подтверждения почты", "<p>123456</p>");

    [Fact]
    public void ResolveSocketOptions_RequiresTls_WithoutDowngradeOrDefault()
    {
        Assert.Equal(
            SecureSocketOptions.StartTls,
            SmtpEmailTransport.ResolveSocketOptions(SmtpSecurity.StartTls));
        Assert.Equal(
            SecureSocketOptions.SslOnConnect,
            SmtpEmailTransport.ResolveSocketOptions(SmtpSecurity.Ssl));
    }

    [Fact]
    public async Task SendAsync_WithServerWithoutRequiredStartTls_FailsAsTlsUnavailable()
    {
        await using var server = SmtpTestServer.Start(SmtpTestServer.Behaviour.NoStartTls);
        var transport = new SmtpEmailTransport(Options(server.Port));

        var error = await Assert.ThrowsAsync<EmailDeliveryException>(
            () => transport.SendAsync(Message()));

        Assert.Equal(EmailFailureReason.TlsUnavailable, error.Reason);
        Assert.False(server.MessageReceived);
    }

    [Fact]
    public async Task SendAsync_WithUntrustedCertificate_FailsWithoutBypassingValidation()
    {
        await using var server = SmtpTestServer.Start(SmtpTestServer.Behaviour.AdvertiseStartTls);
        var transport = new SmtpEmailTransport(Options(server.Port));

        var error = await Assert.ThrowsAsync<EmailDeliveryException>(
            () => transport.SendAsync(Message()));

        Assert.Equal(EmailFailureReason.Certificate, error.Reason);
        Assert.False(server.MessageReceived);
        Assert.DoesNotContain("123456", error.Message);
        Assert.DoesNotContain("user@example.com", error.Message);
    }

    [Fact]
    public async Task SendAsync_WithTrustedSecureServer_DeliversOverRequiredStartTls()
    {
        await using var server = SmtpTestServer.Start(SmtpTestServer.Behaviour.AdvertiseStartTls);
        var transport = new SmtpEmailTransport(
            Options(server.Port),
            () => new SmtpClient
            {
                // Тестовый серт self-signed; проверка сертификата в production
                // остаётся включённой (это подмена только в тесте).
                ServerCertificateValidationCallback = (_, _, _, _) => true
            });

        await transport.SendAsync(Message());

        Assert.True(server.TlsUsed);
        Assert.True(server.MessageReceived);
    }

    [Fact]
    public async Task SendAsync_WhenServerNeverGreets_TimesOutWithBoundedWait()
    {
        await using var server = SmtpTestServer.Start(SmtpTestServer.Behaviour.Silent);
        var options = Options(server.Port);
        options.TimeoutSeconds = 1;
        var transport = new SmtpEmailTransport(options);

        var stopwatch = Stopwatch.StartNew();
        var error = await Assert.ThrowsAsync<EmailDeliveryException>(
            () => transport.SendAsync(Message()));
        stopwatch.Stop();

        Assert.Equal(EmailFailureReason.Timeout, error.Reason);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30));
        Assert.False(server.MessageReceived);
    }
}
