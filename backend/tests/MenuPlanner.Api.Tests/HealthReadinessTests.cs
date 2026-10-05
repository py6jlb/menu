using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using MenuPlanner.Api.Health;

namespace MenuPlanner.Api.Tests;

public sealed class HealthReadinessTests
{
    [Fact]
    public async Task Health_RemainsCheapLiveness_WhenDatabaseUnavailable()
    {
        using var factory = new ReadinessFactory();
        factory.Probe.Ready = false;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"ok\"", body);
    }

    [Fact]
    public async Task Ready_IsReady_WhenDatabaseReachable()
    {
        using var factory = new ReadinessFactory();
        factory.Probe.Ready = true;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"ready\"", body);
        Assert.Contains("\"service\":\"menu-planner-api\"", body);
    }

    [Fact]
    public async Task Ready_IsUnavailable_WhenDatabaseUnavailable()
    {
        using var factory = new ReadinessFactory();
        factory.Probe.Ready = false;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"not-ready\"", body);
    }

    [Fact]
    public async Task Ready_Recovers_WhenDatabaseReturns_WithoutRestart()
    {
        using var factory = new ReadinessFactory();
        using var client = factory.CreateClient();

        factory.Probe.Ready = false;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/ready")).StatusCode);

        factory.Probe.Ready = true;
        var recovered = await client.GetAsync("/ready");

        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Contains("\"status\":\"ready\"", await recovered.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ready_IsUnavailable_WhenProbeThrows_AndDoesNotLeakSecrets()
    {
        const string secret = "Host=db;Database=menu;Username=menu;Password=super-secret-value";
        using var factory = new ReadinessFactory();
        factory.Probe.Throw = new InvalidOperationException(secret);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("super-secret-value", body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.DoesNotContain("Password", body);
    }

    [Fact]
    public async Task Ready_TimesOut_WithBoundedWait()
    {
        using var factory = new ReadinessFactory { TimeoutSeconds = 1 };
        factory.Probe.Hang = true;
        using var client = factory.CreateClient();

        var started = Stopwatch.GetTimestamp();
        var response = await client.GetAsync("/ready");
        var elapsed = Stopwatch.GetElapsedTime(started);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(elapsed < TimeSpan.FromSeconds(5), $"readiness ответила за {elapsed}");
    }

    private sealed class ReadinessFactory : ApiFactory
    {
        public ControllableReadinessProbe Probe { get; } = new();

        public int TimeoutSeconds { get; set; } = ReadinessOptions.DefaultTimeoutSeconds;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["READINESS_TIMEOUT_SECONDS"] = TimeoutSeconds.ToString()
                }));

            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(IDatabaseReadinessProbe));
                if (descriptor is not null)
                    services.Remove(descriptor);

                services.AddSingleton<IDatabaseReadinessProbe>(Probe);
            });
        }
    }

    private sealed class ControllableReadinessProbe : IDatabaseReadinessProbe
    {
        public volatile bool Ready = true;
        public Exception? Throw;
        public bool Hang;

        public async Task<bool> CanConnectAsync(CancellationToken cancellationToken)
        {
            if (Hang)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            if (Throw is not null)
                throw Throw;

            return Ready;
        }
    }
}
