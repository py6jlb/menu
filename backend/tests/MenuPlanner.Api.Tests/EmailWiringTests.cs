using Microsoft.Extensions.DependencyInjection;
using Xunit;
using MenuPlanner.Api.Emails;

namespace MenuPlanner.Api.Tests;

public sealed class EmailWiringTests
{
    [Fact]
    public async Task WithoutSmtpConfigured_ResolvesLoggingTransport_AndEmailSenderSends()
    {
        using var factory = new ApiFactory();

        var transport = factory.Services.GetRequiredService<IEmailTransport>();
        Assert.IsType<LoggingEmailTransport>(transport);

        var sender = factory.Services.GetRequiredService<EmailSender>();
        await sender.SendVerificationCodeAsync("user@example.com", "123456");
    }
}
