using Microsoft.Extensions.Logging;
using Xunit;
using MenuPlanner.Api.Emails;

namespace MenuPlanner.Api.Tests;

public sealed class LoggingEmailTransportTests
{
    private sealed class RecordingLogger : ILogger<LoggingEmailTransport>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add($"{logLevel}: {formatter(state, exception)}");
        }
    }

    [Fact]
    public async Task SendAsync_WritesRecipientAndSubjectToLog_WhenSmtpUnconfigured()
    {
        var logger = new RecordingLogger();
        var transport = new LoggingEmailTransport(logger);

        await transport.SendAsync(new EmailMessage(
            "user@example.com", "noreply@menu.local", "Меню для домохозяек",
            "Код подтверждения почты", "<p>123456</p>"));

        var info = Assert.Single(logger.Messages, m => m.StartsWith("Information"));
        Assert.Contains("user@example.com", info);
        Assert.Contains("Код подтверждения почты", info);
        Assert.Contains("SMTP не настроен", info);
        Assert.Contains("<p>123456</p>", info);
    }
}
