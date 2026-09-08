using Xunit;
using MenuPlanner.Api.Emails;

namespace MenuPlanner.Api.Tests;

public sealed class EmailSenderTests
{
    private static readonly EmailOptions TestOptions = new()
    {
        From = "noreply@menu.local",
        FromName = "Меню для домохозяек"
    };

    private sealed class RecordingTransport : IEmailTransport
    {
        public List<EmailMessage> Sent { get; } = new();

        public Task SendAsync(EmailMessage message)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task SendVerificationCodeAsync_SendsToRecipient_WithCodeInRussianHtml()
    {
        var transport = new RecordingTransport();
        var sender = new EmailSender(TestOptions, transport);

        await sender.SendVerificationCodeAsync("user@example.com", "123456");

        var message = Assert.Single(transport.Sent);
        Assert.Equal("user@example.com", message.To);
        Assert.Equal("noreply@menu.local", message.From);
        Assert.Equal("Меню для домохозяек", message.FromName);
        Assert.Contains("Код подтверждения", message.Subject);
        Assert.Contains("123456", message.HtmlBody);
        Assert.Contains("<!DOCTYPE html>", message.HtmlBody);
        Assert.Contains("подтвержден", message.HtmlBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendPasswordResetCodeAsync_SendsToRecipient_WithCodeInRussianHtml()
    {
        var transport = new RecordingTransport();
        var sender = new EmailSender(TestOptions, transport);

        await sender.SendPasswordResetCodeAsync("user@example.com", "654321");

        var message = Assert.Single(transport.Sent);
        Assert.Equal("user@example.com", message.To);
        Assert.Contains("парол", message.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("654321", message.HtmlBody);
        Assert.Contains("<!DOCTYPE html>", message.HtmlBody);
    }
}
