using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Emails;

namespace MenuPlanner.Api.Tests;

public sealed class EmailVerificationFlowTests
{
    private static readonly Regex CodeRegex = new(@"class=""code"">(\d{6})<", RegexOptions.Compiled);

    [Fact]
    public async Task Register_ReturnsUnverifiedUser_AndSendsVerificationCodeEmail()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        var email = $"verify-{Guid.NewGuid():N}@example.com";

        var register = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "secret1" });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        Assert.Equal(email, auth.User.Email);
        Assert.False(auth.User.IsEmailVerified);
        Assert.False(string.IsNullOrWhiteSpace(auth.Token));

        var letter = Assert.Single(factory.Emails);
        Assert.Equal(email, letter.To);
        Assert.Contains("Код подтверждения почты", letter.Subject);
        Assert.Matches(CodeRegex, letter.HtmlBody);
    }

    [Fact]
    public async Task Verify_WithCorrectCode_ReturnsVerifiedUser_AndRepeatIsConflict()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        var email = $"verify-ok-{Guid.NewGuid():N}@example.com";
        var token = await RegisterAsync(client, email);
        var code = CodeFrom(factory);

        var verify = await PostVerifyAsync(client, token, code);
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var verified = await verify.Content.ReadFromJsonAsync<UserDto>();
        Assert.NotNull(verified);
        Assert.True(verified.IsEmailVerified);

        var repeat = await PostVerifyAsync(client, token, code);
        Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
    }

    [Fact]
    public async Task Verify_AfterFiveWrongAttempts_LocksOperation_EvenForCorrectCode()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        var email = $"verify-lock-{Guid.NewGuid():N}@example.com";
        var token = await RegisterAsync(client, email);
        var code = CodeFrom(factory);

        for (var i = 0; i < 4; i++)
        {
            var wrong = await PostVerifyAsync(client, token, "000000");
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        }

        var fifth = await PostVerifyAsync(client, token, "000000");
        Assert.Equal(HttpStatusCode.Locked, fifth.StatusCode);

        var correctWhileLocked = await PostVerifyAsync(client, token, code);
        Assert.Equal(HttpStatusCode.Locked, correctWhileLocked.StatusCode);
    }

    [Fact]
    public async Task Resend_WithinCooldown_ReturnsTooManyRequests()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        var email = $"verify-resend-{Guid.NewGuid():N}@example.com";
        var token = await RegisterAsync(client, email);

        var resend = await PostAuthorizedAsync(client, token, "/api/auth/verify/resend", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, resend.StatusCode);
    }

    [Fact]
    public async Task Resend_AfterCooldown_SendsNewCode_AndInvalidatesOld()
    {
        using var factory = new AuthApiFactory()
            .WithConfig("AUTH_CODE_RESEND_COOLDOWN_MINUTES", "0");
        using var client = factory.CreateClient();
        var email = $"verify-resend-new-{Guid.NewGuid():N}@example.com";
        var token = await RegisterAsync(client, email);
        var oldCode = CodeFrom(factory);

        var resend = await PostAuthorizedAsync(client, token, "/api/auth/verify/resend", null);
        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);

        var newCode = CodeFrom(factory);
        Assert.Equal(2, factory.Emails.Count);
        Assert.NotEqual(oldCode, newCode);

        var oldVerifies = await PostVerifyAsync(client, token, oldCode);
        Assert.Equal(HttpStatusCode.BadRequest, oldVerifies.StatusCode);

        var newVerifies = await PostVerifyAsync(client, token, newCode);
        Assert.Equal(HttpStatusCode.OK, newVerifies.StatusCode);
    }

    [Fact]
    public async Task Resend_IsRateLimited_ToFivePerHour_OnEmailAndIp()
    {
        using var factory = new AuthApiFactory()
            .WithConfig("AUTH_CODE_RESEND_COOLDOWN_MINUTES", "0");
        using var client = factory.CreateClient();
        var emailA = $"verify-rate-a-{Guid.NewGuid():N}@example.com";
        var tokenA = await RegisterAsync(client, emailA);

        for (var i = 0; i < 5; i++)
        {
            var resend = await PostAuthorizedAsync(client, tokenA, "/api/auth/verify/resend", null);
            Assert.Equal(HttpStatusCode.OK, resend.StatusCode);
        }

        var sixth = await PostAuthorizedAsync(client, tokenA, "/api/auth/verify/resend", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);

        var emailB = $"verify-rate-b-{Guid.NewGuid():N}@example.com";
        var tokenB = await RegisterAsync(client, emailB);
        var resendFromOtherEmail = await PostAuthorizedAsync(client, tokenB, "/api/auth/verify/resend", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, resendFromOtherEmail.StatusCode);
    }

    [Fact]
    public async Task Resend_WhileLocked_ReturnsLocked()
    {
        using var factory = new AuthApiFactory()
            .WithConfig("AUTH_CODE_RESEND_COOLDOWN_MINUTES", "0");
        using var client = factory.CreateClient();
        var email = $"verify-resend-locked-{Guid.NewGuid():N}@example.com";
        var token = await RegisterAsync(client, email);

        for (var i = 0; i < 5; i++)
            await PostVerifyAsync(client, token, "000000");

        var resend = await PostAuthorizedAsync(client, token, "/api/auth/verify/resend", null);
        Assert.Equal(HttpStatusCode.Locked, resend.StatusCode);
    }

    private static async Task<string> RegisterAsync(HttpClient client, string email)
    {
        var register = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "secret1" });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        Assert.False(string.IsNullOrWhiteSpace(auth.Token));
        return auth.Token;
    }

    private static Task<HttpResponseMessage> PostVerifyAsync(HttpClient client, string token, string code) =>
        PostAuthorizedAsync(client, token, "/api/auth/verify", new { code });

    private static async Task<HttpResponseMessage> PostAuthorizedAsync(
        HttpClient client, string token, string url, object? body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(body ?? new { });
        return await client.SendAsync(request);
    }

    private static string CodeFrom(AuthApiFactory factory)
    {
        var letter = factory.Emails.Last();
        var match = CodeRegex.Match(letter.HtmlBody);
        Assert.True(match.Success, "Код не найден в теле письма.");
        return match.Groups[1].Value;
    }
}

internal sealed class AuthApiFactory : ApiFactory
{
    private readonly Dictionary<string, string?> _config = new();

    public AuthApiFactory() => AutoVerifyEmailsOnRegistration = false;

    public RecordingEmailTransport Transport { get; } = new();

    public IReadOnlyList<EmailMessage> Emails => Transport.Emails;

    public AuthApiFactory WithConfig(string key, string value)
    {
        _config[key] = value;
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(_config));

        builder.ConfigureServices(services =>
        {
            var transportDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IEmailTransport));
            if (transportDescriptor is not null)
                services.Remove(transportDescriptor);
            services.AddSingleton<IEmailTransport>(Transport);

            var optionsDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(AuthCodeOptions));
            if (optionsDescriptor is not null)
                services.Remove(optionsDescriptor);
            services.AddSingleton(new AuthCodeOptions
            {
                MaxAttempts = IntOf("AUTH_CODE_MAX_ATTEMPTS", AuthCodeOptions.DefaultMaxAttempts),
                LockDurationDays = IntOf("AUTH_CODE_LOCK_DAYS", AuthCodeOptions.DefaultLockDurationDays),
                ResendCooldownMinutes = IntOf("AUTH_CODE_RESEND_COOLDOWN_MINUTES", AuthCodeOptions.DefaultResendCooldownMinutes),
                ResendRateLimitPerHour = IntOf("AUTH_CODE_RESEND_RATE_LIMIT_PER_HOUR", AuthCodeOptions.DefaultResendRateLimitPerHour)
            });
        });
    }

    private int IntOf(string key, int fallback) =>
        _config.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;
}

internal sealed class RecordingEmailTransport : IEmailTransport
{
    private readonly object _gate = new();
    private readonly List<EmailMessage> _emails = new();

    public IReadOnlyList<EmailMessage> Emails
    {
        get
        {
            lock (_gate)
                return _emails.ToList();
        }
    }

    public Task SendAsync(EmailMessage message)
    {
        lock (_gate)
            _emails.Add(message);
        return Task.CompletedTask;
    }
}
