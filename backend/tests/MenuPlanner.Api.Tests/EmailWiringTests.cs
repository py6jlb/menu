using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Emails;

namespace MenuPlanner.Api.Tests;

public sealed class EmailWiringTests
{
    private const string StrongJwt = "N7q2Zx9Kp4Rt6Vw8Yb3Mc5Ld1Fh0Jg2S";

    private sealed class FailingEmailTransport : IEmailTransport
    {
        private readonly Func<EmailMessage, bool> _shouldFail;

        public FailingEmailTransport(Func<EmailMessage, bool> shouldFail) => _shouldFail = shouldFail;

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) =>
            _shouldFail(message)
                ? throw new EmailDeliveryException(EmailFailureReason.Connection, "SocketException")
                : Task.CompletedTask;
    }

    private static ApiFactory ProductionLabWithSmtp()
    {
        var factory = new ApiFactory();
        factory.Settings["DEPLOYMENT_MODE"] = "production";
        factory.Settings["PUBLIC_BASE_URL"] = "https://menu.example.com";
        factory.Settings["SMTP_HOST"] = "smtp.example.com";
        factory.Settings["SMTP_FROM"] = "noreply@example.com";
        factory.Settings["JWT_SECRET"] = StrongJwt;
        return factory;
    }

    [Fact]
    public async Task WithoutSmtpConfigured_ResolvesLoggingTransport_AndEmailSenderSends()
    {
        using var factory = new ApiFactory();

        var transport = factory.Services.GetRequiredService<IEmailTransport>();
        Assert.IsType<LoggingEmailTransport>(transport);

        var sender = factory.Services.GetRequiredService<EmailSender>();
        await sender.SendVerificationCodeAsync("user@example.com", "123456");
    }

    [Fact]
    public void ProductionWithCompleteConfiguration_ResolvesSmtpTransport()
    {
        using var factory = ProductionLabWithSmtp();

        var transport = factory.Services.GetRequiredService<IEmailTransport>();

        Assert.IsType<SmtpEmailTransport>(transport);
    }

    [Fact]
    public void ProductionWithLogTransport_IsRejectedAtStartup()
    {
        using var factory = ProductionLabWithSmtp();
        factory.Settings["EMAIL_TRANSPORT"] = "log";

        var error = Assert.ThrowsAny<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("EMAIL_TRANSPORT", Flatten(error));
    }

    [Fact]
    public async Task Register_WhenTransportFails_StillReturnsSessionAndQueuesDelivery()
    {
        using var factory = new ApiFactory { AutoVerifyEmailsOnRegistration = false };
        factory.ConfigureTestServices = services =>
            services.AddSingleton<IEmailTransport>(new FailingEmailTransport(_ => true));
        using var client = factory.CreateClient();
        var email = $"fail-{Guid.NewGuid():N}@example.com";

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", new { email, password = "secret1" });

        // Временный отказ SMTP не маскирует создание аккаунта: сессия выдана,
        // а принятая доставка осталась в очереди на повтор.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        Assert.False(string.IsNullOrWhiteSpace(auth.Token));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queued = await db.EmailOutboxMessages.SingleAsync();
        Assert.Equal(EmailOutboxStatus.Pending, queued.Status);
        Assert.Equal(1, queued.Attempts);
    }

    [Fact]
    public async Task Forgot_WhenTransportFails_StaysNeutral()
    {
        using var factory = new ApiFactory();
        factory.ConfigureTestServices = services => services.AddSingleton<IEmailTransport>(
            new FailingEmailTransport(m => m.Subject.Contains("парол", StringComparison.OrdinalIgnoreCase)));
        using var client = factory.CreateClient();
        var email = $"forgot-fail-{Guid.NewGuid():N}@example.com";

        var register = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "secret1" });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var response = await client.PostAsJsonAsync("/api/auth/forgot", new { email });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageDto>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.Message));
    }

    private static string Flatten(Exception exception)
    {
        var text = exception.Message;
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
            text += " | " + inner.Message;
        return text;
    }
}
