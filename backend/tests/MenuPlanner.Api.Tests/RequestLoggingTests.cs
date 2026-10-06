using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MenuPlanner.Api.Tests;

public sealed class RequestLoggingTests
{
    [Fact]
    public async Task Request_LogsOperationTraceAndRelease_WithoutShareToken()
    {
        const string token = "secret-share-token-9f2c";
        using var factory = new ApiFactory();
        var capture = new CapturingLoggerProvider();
        factory.Settings[Observability.ReleaseIdentity.IdVariable] = "release-42";
        factory.ConfigureTestServices = services =>
            services.AddSingleton<ILoggerProvider>(capture);
        using var client = factory.CreateClient();

        await client.GetAsync($"/api/shared/{token}");

        var entry = capture.Logs.LastOrDefault(l =>
            l.Properties.ContainsKey("Operation")
            && l.Properties.ContainsKey("TraceId")
            && l.Properties.ContainsKey("ReleaseId"));
        Assert.NotNull(entry);
        Assert.Equal("/api/shared/{token}", entry!.Properties["Operation"]);
        Assert.False(string.IsNullOrWhiteSpace(entry.Properties["TraceId"]?.ToString()));
        Assert.Equal("release-42", entry.Properties["ReleaseId"]);
        Assert.DoesNotContain(token, entry.Message);
        foreach (var log in capture.Logs)
            Assert.DoesNotContain(token, log.Message);
    }

    [Fact]
    public async Task Request_LogsReleaseId_AsUnknown_WhenNotConfigured()
    {
        using var factory = new ApiFactory();
        var capture = new CapturingLoggerProvider();
        factory.ConfigureTestServices = services =>
            services.AddSingleton<ILoggerProvider>(capture);
        using var client = factory.CreateClient();

        await client.GetAsync("/health");

        var entry = Assert.Single(capture.Logs, l => l.Properties.ContainsKey("Operation"));
        Assert.Equal(Observability.ReleaseIdentity.UnknownId, entry.Properties["ReleaseId"]);
    }

    [Fact]
    public async Task AuthRequest_DoesNotLogCredentialsOrCode()
    {
        const string email = "secret-person@example.com";
        const string password = "P@ssw0rd-leak-check";
        const string code = "424242";
        using var factory = new ApiFactory();
        var capture = new CapturingLoggerProvider();
        factory.ConfigureTestServices = services =>
            services.AddSingleton<ILoggerProvider>(capture);
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        await client.PostAsJsonAsync("/api/auth/verify", new { code });

        Assert.DoesNotContain(capture.Logs, l => l.Message.Contains(password));
        Assert.DoesNotContain(capture.Logs, l => l.Message.Contains(email));
        Assert.DoesNotContain(capture.Logs, l => l.Message.Contains(code));
    }
}
