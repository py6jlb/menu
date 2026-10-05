using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Configuration;

namespace MenuPlanner.Api.Tests;

public sealed class AuthRateLimitPolicyTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    [Fact]
    public void Check_WhenIpLimited_DoesNotCreateNewIdentityKey()
    {
        var limiter = new FixedWindowRateLimiter();

        Assert.Null(AuthRateLimitPolicy.Check(
            limiter, "login", "1.1.1.1", perIp: 1, perIdentity: 100, identity: "a", Hour, Now));
        Assert.Equal(2, limiter.ActiveKeyCount);

        var blocked = AuthRateLimitPolicy.Check(
            limiter, "login", "1.1.1.1", perIp: 1, perIdentity: 100, identity: "b", Hour, Now);

        Assert.NotNull(blocked);
        Assert.Equal(2, limiter.ActiveKeyCount);
    }

    [Fact]
    public void Check_WhenIdentityLimited_BlocksThatIdentityOnly()
    {
        var limiter = new FixedWindowRateLimiter();

        Assert.Null(AuthRateLimitPolicy.Check(
            limiter, "login", "1.1.1.1", perIp: 100, perIdentity: 1, identity: "a", Hour, Now));
        Assert.NotNull(AuthRateLimitPolicy.Check(
            limiter, "login", "1.1.1.1", perIp: 100, perIdentity: 1, identity: "a", Hour, Now));
        Assert.Null(AuthRateLimitPolicy.Check(
            limiter, "login", "1.1.1.1", perIp: 100, perIdentity: 1, identity: "b", Hour, Now));
    }
}

public sealed class ClientIpResolverTests
{
    [Fact]
    public void Resolve_IgnoresForgedForwardedForHeader()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.9";

        Assert.Equal("10.0.0.5", ClientIpResolver.Resolve(context));
    }
}

public sealed class ForwardedHeaderConfigurationTests
{
    private static ForwardedHeadersOptions Build(params (string Key, string Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();
        return ForwardedHeaderConfiguration.Build(configuration);
    }

    private static async Task<DefaultHttpContext> ApplyAsync(
        ForwardedHeadersOptions options, string peer, string? forwardedFor)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        if (forwardedFor is not null)
            context.Request.Headers["X-Forwarded-For"] = forwardedFor;

        var middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            NullLoggerFactory.Instance,
            Options.Create(options));
        await middleware.Invoke(context);
        return context;
    }

    [Fact]
    public void Build_WithNoTrustedProxies_LeavesEmptyTrustSet()
    {
        var options = ForwardedHeaderConfiguration.Build(new ConfigurationBuilder().Build());

        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
        Assert.False(ForwardedHeaderConfiguration.HasTrustedProxies(options));
    }

    [Fact]
    public void Build_ParsesAddressesAndNetworks()
    {
        var options = Build(
            ("TRUSTED_PROXY_ADDRESSES", "10.0.0.9, 10.0.0.10"),
            ("TRUSTED_PROXY_NETWORKS", "172.28.0.0/16"));

        Assert.Equal(2, options.KnownProxies.Count);
        Assert.Single(options.KnownIPNetworks);
        Assert.True(ForwardedHeaderConfiguration.HasTrustedProxies(options));
    }

    [Theory]
    [InlineData("not-a-cidr")]
    [InlineData("172.28.0.0/33")]
    [InlineData("172.28.0.0")]
    public void Build_WithMalformedNetwork_Throws(string value)
    {
        Assert.Throws<InvalidOperationException>(() => Build(("TRUSTED_PROXY_NETWORKS", value)));
    }

    [Fact]
    public void Build_WithMalformedAddress_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Build(("TRUSTED_PROXY_ADDRESSES", "nope")));
    }

    [Fact]
    public async Task Middleware_IgnoresForwardedFor_FromUntrustedPeer()
    {
        var options = Build(("TRUSTED_PROXY_NETWORKS", "172.28.0.0/16"));

        var context = await ApplyAsync(options, peer: "203.0.113.5", forwardedFor: "9.9.9.9");

        Assert.Equal("203.0.113.5", context.Connection.RemoteIpAddress!.ToString());
    }

    [Fact]
    public async Task Middleware_HonoursForwardedFor_FromTrustedEdge()
    {
        var options = Build(("TRUSTED_PROXY_NETWORKS", "172.28.0.0/16"));

        var context = await ApplyAsync(options, peer: "172.28.0.7", forwardedFor: "9.9.9.9");

        Assert.Equal("9.9.9.9", context.Connection.RemoteIpAddress!.ToString());
    }
}

public sealed class AuthRateLimitOptionsTests
{
    [Fact]
    public void Read_AppliesEnvironmentOverrides()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AUTH_RATE_LIMIT_LOGIN_PER_IP_PER_MINUTE"] = "7",
                ["AUTH_RATE_LIMIT_MAX_TRACKED_KEYS"] = "123"
            })
            .Build();

        var options = AuthRateLimitOptions.Read(configuration);

        Assert.Equal(7, options.LoginPerIpPerMinute);
        Assert.Equal(123, options.MaxTrackedKeys);
    }

    [Theory]
    [InlineData("AUTH_RATE_LIMIT_LOGIN_PER_IP_PER_MINUTE", "0")]
    [InlineData("AUTH_RATE_LIMIT_MAX_TRACKED_KEYS", "-1")]
    public void Read_WithNonPositiveValue_ThrowsWithoutSecrets(string key, string value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
            .Build();

        var error = Assert.Throws<InvalidOperationException>(() => AuthRateLimitOptions.Read(configuration));
        Assert.Contains("должен быть положительным", error.Message);
    }

    [Fact]
    public void Read_WithNonNumericValue_Throws()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AUTH_RATE_LIMIT_LOGIN_PER_IP_PER_MINUTE"] = "many"
            })
            .Build();

        var error = Assert.Throws<InvalidOperationException>(() => AuthRateLimitOptions.Read(configuration));
        Assert.Contains("целым числом", error.Message);
    }
}

public sealed class AuthRateLimitFlowTests
{
    [Fact]
    public async Task Login_IsRateLimited_ByEmail_WithRetryAfter()
    {
        using var factory = new ApiFactory();
        factory.Settings["AUTH_RATE_LIMIT_LOGIN_PER_IP_PER_MINUTE"] = "100";
        factory.Settings["AUTH_RATE_LIMIT_LOGIN_PER_EMAIL_PER_MINUTE"] = "2";
        using var client = factory.CreateClient();
        var email = $"login-rate-{Guid.NewGuid():N}@example.com";
        await client.PostAsJsonAsync("/api/auth/register", new { email, password = "secret1" });

        for (var i = 0; i < 2; i++)
        {
            var attempt = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong" });
            Assert.Equal(HttpStatusCode.Unauthorized, attempt.StatusCode);
        }

        var third = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong" });
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.True(third.Headers.TryGetValues("Retry-After", out var values));
        Assert.True(int.Parse(values!.First()) >= 1);
    }

    [Fact]
    public async Task Register_IsRateLimited_ByIp()
    {
        using var factory = new ApiFactory();
        factory.Settings["AUTH_RATE_LIMIT_REGISTER_PER_IP_PER_HOUR"] = "2";
        factory.Settings["AUTH_RATE_LIMIT_REGISTER_PER_EMAIL_PER_HOUR"] = "100";
        using var client = factory.CreateClient();

        for (var i = 0; i < 2; i++)
        {
            var created = await client.PostAsJsonAsync("/api/auth/register",
                new { email = $"reg-rate-{i}-{Guid.NewGuid():N}@example.com", password = "secret1" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        var third = await client.PostAsJsonAsync("/api/auth/register",
            new { email = $"reg-rate-3-{Guid.NewGuid():N}@example.com", password = "secret1" });
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    [Fact]
    public async Task Forgot_ForgedForwardedFor_DoesNotBypassIpLimit()
    {
        using var factory = new ApiFactory();
        factory.Settings["AUTH_CODE_RESEND_COOLDOWN_MINUTES"] = "0";
        factory.Settings["AUTH_CODE_RESEND_RATE_LIMIT_PER_HOUR"] = "5";
        using var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/forgot");
            request.Headers.Add("X-Forwarded-For", $"203.0.113.{i + 1}");
            request.Content = JsonContent.Create(new { email = $"forged-{i}@example.com" });
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var sixth = new HttpRequestMessage(HttpMethod.Post, "/api/auth/forgot");
        sixth.Headers.Add("X-Forwarded-For", "203.0.113.200");
        sixth.Content = JsonContent.Create(new { email = $"forged-99@example.com" });
        var blocked = await client.SendAsync(sixth);

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.TryGetValues("Retry-After", out _));
    }

    [Fact]
    public async Task Reset_IsRateLimited_ByIp()
    {
        using var factory = new ApiFactory();
        factory.Settings["AUTH_RATE_LIMIT_RESET_PER_IP_PER_HOUR"] = "2";
        factory.Settings["AUTH_RATE_LIMIT_RESET_PER_EMAIL_PER_HOUR"] = "100";
        using var client = factory.CreateClient();

        for (var i = 0; i < 2; i++)
        {
            var attempt = await client.PostAsJsonAsync("/api/auth/reset", new
            {
                email = $"reset-rate-{i}@example.com",
                code = "000000",
                newPassword = "newsecret1",
                newPasswordConfirm = "newsecret1"
            });
            Assert.Equal(HttpStatusCode.BadRequest, attempt.StatusCode);
        }

        var third = await client.PostAsJsonAsync("/api/auth/reset", new
        {
            email = "reset-rate-3@example.com",
            code = "000000",
            newPassword = "newsecret1",
            newPasswordConfirm = "newsecret1"
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }
}
