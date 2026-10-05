using Microsoft.Extensions.Configuration;
using Xunit;
using MenuPlanner.Api.Emails;

namespace MenuPlanner.Api.Tests;

public sealed class EmailConfigurationTests
{
    private static EmailOptions Read(params (string Key, string? Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["SMTP_HOST"] = "smtp.example.com",
            ["SMTP_FROM"] = "noreply@example.com"
        };
        foreach (var pair in overrides) values[pair.Key] = pair.Value;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return EmailConfiguration.Read(configuration);
    }

    [Fact]
    public void CompleteConfiguration_DefaultsToRequiredStartTlsOnPort587()
    {
        var options = Read();

        Assert.Equal(EmailTransportMode.Smtp, options.Transport);
        Assert.Equal(SmtpSecurity.StartTls, options.Security);
        Assert.Equal(587, options.Port);
        Assert.Equal(10, options.TimeoutSeconds);
    }

    [Fact]
    public void MissingHost_DefaultsToLogTransport_ForExplicitDevUse()
    {
        var options = Read(("SMTP_HOST", ""));

        Assert.Equal(EmailTransportMode.Log, options.Transport);
    }

    [Fact]
    public void ExplicitLogTransport_IsHonoured()
    {
        var options = Read(("EMAIL_TRANSPORT", "log"));

        Assert.Equal(EmailTransportMode.Log, options.Transport);
    }

    [Fact]
    public void ExplicitSmtpTransport_WithoutHost_IsRejected()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Read(("SMTP_HOST", ""), ("EMAIL_TRANSPORT", "smtp")));

        Assert.Contains("SMTP_HOST", error.Message);
        Assert.Contains("log", error.Message);
    }

    [Fact]
    public void SslSecurity_WithImplicitTlsPort_IsAccepted()
    {
        var options = Read(("SMTP_SECURITY", "ssl"), ("SMTP_PORT", "465"));

        Assert.Equal(SmtpSecurity.Ssl, options.Security);
    }

    [Fact]
    public void StartTlsSecurity_WithImplicitTlsPort_IsRejected()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Read(("SMTP_PORT", "465")));

        Assert.Contains("SMTP_PORT", error.Message);
        Assert.Contains("ssl", error.Message);
    }

    [Fact]
    public void SslSecurity_WithStartTlsPort_IsRejected()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Read(("SMTP_SECURITY", "ssl"), ("SMTP_PORT", "587")));

        Assert.Contains("SMTP_PORT", error.Message);
        Assert.Contains("starttls", error.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("not-a-port")]
    public void InvalidPort_IsRejected(string port)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Read(("SMTP_PORT", port)));

        Assert.Contains("SMTP_PORT", error.Message);
    }

    [Fact]
    public void InvalidSecurityMode_IsRejected()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Read(("SMTP_SECURITY", "when-available")));

        Assert.Contains("SMTP_SECURITY", error.Message);
    }

    [Fact]
    public void LegacyStartTlsDisable_IsRejected()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Read(("SMTP_ENABLE_STARTTLS", "false")));

        Assert.Contains("SMTP_ENABLE_STARTTLS", error.Message);
    }

    [Fact]
    public void MissingFrom_IsRejected()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Read(("SMTP_FROM", "")));

        Assert.Contains("SMTP_FROM", error.Message);
    }

    [Fact]
    public void UserWithoutPassword_IsRejected()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Read(("SMTP_USER", "mailer"), ("SMTP_PASSWORD", "")));

        Assert.Contains("SMTP_USER", error.Message);
        Assert.Contains("SMTP_PASSWORD", error.Message);
    }

    [Fact]
    public void PasswordWithoutUser_IsRejected()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Read(("SMTP_USER", ""), ("SMTP_PASSWORD", "s3cret-value")));

        Assert.Contains("SMTP_USER", error.Message);
    }

    [Fact]
    public void InvalidTimeout_IsRejected()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Read(("SMTP_TIMEOUT_SECONDS", "0")));

        Assert.Contains("SMTP_TIMEOUT_SECONDS", error.Message);
    }

    [Fact]
    public void InvalidTransport_IsRejected()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Read(("EMAIL_TRANSPORT", "carrier-pigeon")));

        Assert.Contains("EMAIL_TRANSPORT", error.Message);
    }

    [Fact]
    public void ConfigurationErrors_DoNotRevealPassword()
    {
        const string password = "sup3r-secret-password";

        var error = Assert.Throws<InvalidOperationException>(
            () => Read(("SMTP_USER", "mailer"), ("SMTP_PASSWORD", password), ("SMTP_PORT", "465")));

        Assert.DoesNotContain(password, error.Message);
    }
}
