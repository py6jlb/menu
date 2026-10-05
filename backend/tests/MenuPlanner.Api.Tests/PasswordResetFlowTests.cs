using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Emails;

namespace MenuPlanner.Api.Tests;

public sealed class PasswordResetFlowTests
{
    private const string NeutralMessage =
        "Если аккаунт существует и почта подтверждена, отправлен код.";
    private const string ResetSubject = "Восстановление пароля";
    private static readonly Regex CodeRegex = new(@"class=""code"">(\d{6})<", RegexOptions.Compiled);

    [Fact]
    public async Task RequestPasswordReset_ForVerifiedUser_SendsResetCode_AndReturnsNeutralMessage()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        var email = $"reset-forgot-{Guid.NewGuid():N}@example.com";
        await RegisterAsync(client, email);
        await factory.VerifyUserAsync(email);

        var resetRequest = await RequestPasswordResetAsync(client, email);

        Assert.Equal(HttpStatusCode.OK, resetRequest.StatusCode);
        var message = await resetRequest.Content.ReadFromJsonAsync<MessageDto>();
        Assert.NotNull(message);
        Assert.Equal(NeutralMessage, message.Message);

        var letter = Assert.Single(ResetEmails(factory));
        Assert.Equal(email, letter.To);
        Assert.Contains(ResetSubject, letter.Subject);
        Assert.Matches(CodeRegex, letter.HtmlBody);
    }

    [Fact]
    public async Task RequestPasswordReset_ForUnknownOrUnverifiedEmail_ReturnsSameNeutralResponse_AndSendsNothing()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        var unknown = await RequestPasswordResetAsync(client, $"reset-none-{Guid.NewGuid():N}@example.com");
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.Equal(NeutralMessage, (await unknown.Content.ReadFromJsonAsync<MessageDto>())!.Message);

        var email = $"reset-unverified-{Guid.NewGuid():N}@example.com";
        await RegisterAsync(client, email);
        var unverified = await RequestPasswordResetAsync(client, email);
        Assert.Equal(HttpStatusCode.OK, unverified.StatusCode);
        Assert.Equal(NeutralMessage, (await unverified.Content.ReadFromJsonAsync<MessageDto>())!.Message);

        Assert.Empty(ResetEmails(factory));
    }

    [Fact]
    public async Task RequestPasswordReset_WithinCooldown_DoesNotResend()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        var email = $"reset-cooldown-{Guid.NewGuid():N}@example.com";
        await RegisterAsync(client, email);
        await factory.VerifyUserAsync(email);

        Assert.Equal(HttpStatusCode.OK, (await RequestPasswordResetAsync(client, email)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RequestPasswordResetAsync(client, email)).StatusCode);

        Assert.Single(ResetEmails(factory));
    }

    [Fact]
    public async Task RequestPasswordReset_AfterCooldown_SendsNewCode_AndInvalidatesOld()
    {
        using var factory = new AuthApiFactory()
            .WithConfig("AUTH_CODE_RESEND_COOLDOWN_MINUTES", "0");
        using var client = factory.CreateClient();
        var email = $"reset-resend-{Guid.NewGuid():N}@example.com";
        await RegisterAsync(client, email);
        await factory.VerifyUserAsync(email);

        await RequestPasswordResetAsync(client, email);
        var oldCode = ResetCodeFrom(factory);
        await RequestPasswordResetAsync(client, email);
        var newCode = ResetCodeFrom(factory);

        Assert.Equal(2, ResetEmails(factory).Count());
        Assert.NotEqual(oldCode, newCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await ResetAsync(client, email, oldCode, "newsecret1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ResetAsync(client, email, newCode, "newsecret1")).StatusCode);
    }

    [Fact]
    public async Task RequestPasswordReset_IsRateLimited_ToFivePerHour_OnEmailAndIp()
    {
        using var factory = new AuthApiFactory()
            .WithConfig("AUTH_CODE_RESEND_COOLDOWN_MINUTES", "0");
        using var client = factory.CreateClient();
        var emailA = $"reset-rate-a-{Guid.NewGuid():N}@example.com";
        await RegisterAsync(client, emailA);
        await factory.VerifyUserAsync(emailA);

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await RequestPasswordResetAsync(client, emailA)).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await RequestPasswordResetAsync(client, emailA)).StatusCode);

        var emailB = $"reset-rate-b-{Guid.NewGuid():N}@example.com";
        await RegisterAsync(client, emailB);
        await factory.VerifyUserAsync(emailB);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await RequestPasswordResetAsync(client, emailB)).StatusCode);
    }

    [Fact]
    public async Task RequestPasswordReset_AfterFiveWrongAttempts_IsNotLockedOut_AndSendsNewCode()
    {
        using var factory = new AuthApiFactory()
            .WithConfig("AUTH_CODE_RESEND_COOLDOWN_MINUTES", "0");
        using var client = factory.CreateClient();
        var email = $"reset-locked-{Guid.NewGuid():N}@example.com";
        await RegisterAsync(client, email);
        await factory.VerifyUserAsync(email);

        await RequestPasswordResetAsync(client, email);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.BadRequest,
                (await ResetAsync(client, email, "000000", "newsecret1")).StatusCode);

        var resetRequest = await RequestPasswordResetAsync(client, email);

        Assert.Equal(HttpStatusCode.OK, resetRequest.StatusCode);
        Assert.Equal(
            NeutralMessage,
            (await resetRequest.Content.ReadFromJsonAsync<MessageDto>())!.Message);
        Assert.Equal(2, ResetEmails(factory).Count());
    }

    [Fact]
    public async Task Reset_WithCorrectCode_ChangesPassword_AndInvalidatesIssuedTokens()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        var email = $"reset-ok-{Guid.NewGuid():N}@example.com";
        const string oldPassword = "secret1";
        const string newPassword = "newsecret1";
        var token = await RegisterAsync(client, email, oldPassword);
        await factory.VerifyUserAsync(email);

        Assert.Equal(HttpStatusCode.OK, (await GetMeAsync(client, token)).StatusCode);

        await RequestPasswordResetAsync(client, email);
        var code = ResetCodeFrom(factory);

        var reset = await ResetAsync(client, email, code, newPassword);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetMeAsync(client, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login", new { email, password = oldPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/login", new { email, password = newPassword })).StatusCode);
    }

    [Fact]
    public async Task Reset_AfterFiveWrongAttempts_ClosesChallenge_AndNewCodeWorks()
    {
        using var factory = new AuthApiFactory()
            .WithConfig("AUTH_CODE_RESEND_COOLDOWN_MINUTES", "0");
        using var client = factory.CreateClient();
        var email = $"reset-wrong-{Guid.NewGuid():N}@example.com";
        await RegisterAsync(client, email);
        await factory.VerifyUserAsync(email);
        await RequestPasswordResetAsync(client, email);
        var oldCode = ResetCodeFrom(factory);

        for (var i = 0; i < 4; i++)
            Assert.Equal(HttpStatusCode.BadRequest,
                (await ResetAsync(client, email, "000000", "newsecret1")).StatusCode);

        var closed = await ResetAsync(client, email, "000000", "newsecret1");
        Assert.Equal(HttpStatusCode.BadRequest, closed.StatusCode);
        var closedError = await closed.Content.ReadFromJsonAsync<ResetErrorDto>();
        Assert.NotNull(closedError);
        Assert.Equal("closed", closedError.Code);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await ResetAsync(client, email, oldCode, "newsecret1")).StatusCode);

        await RequestPasswordResetAsync(client, email);
        var newCode = ResetCodeFrom(factory);
        Assert.Equal(HttpStatusCode.OK, (await ResetAsync(client, email, newCode, "newsecret1")).StatusCode);
    }

    [Fact]
    public async Task Reset_WithoutChallenge_DoesNotLockAccount_ForFiveRequests()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        var email = $"reset-nochallenge-{Guid.NewGuid():N}@example.com";
        const string password = "secret1";
        await RegisterAsync(client, email, password);
        await factory.VerifyUserAsync(email);

        for (var i = 0; i < 5; i++)
        {
            var result = await ResetAsync(client, email, "000000", "newsecret1");
            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
            var error = await result.Content.ReadFromJsonAsync<ResetErrorDto>();
            Assert.NotNull(error);
            Assert.Equal("invalid", error.Code);
        }

        // Пароль не менялся, аккаунт не заблокирован для восстановления.
        await RequestPasswordResetAsync(client, email);
        var code = ResetCodeFrom(factory);
        Assert.Equal(HttpStatusCode.OK, (await ResetAsync(client, email, code, "newsecret2")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "newsecret2" })).StatusCode);
    }

    [Fact]
    public async Task Reset_WithMismatchedOrShortPassword_ReturnsBadRequest_AndKeepsPassword()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        var email = $"reset-confirm-{Guid.NewGuid():N}@example.com";
        await RegisterAsync(client, email);
        await factory.VerifyUserAsync(email);
        await RequestPasswordResetAsync(client, email);
        var code = ResetCodeFrom(factory);

        var mismatch = await ResetAsync(client, email, code, "newsecret1", confirm: "different1");
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);

        var tooShort = await ResetAsync(client, email, code, "123", confirm: "123");
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "secret1" })).StatusCode);
    }

    [Fact]
    public async Task Reset_ForUnknownOrUnverifiedEmail_ReturnsGenericBadRequest()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        var unknown = await ResetAsync(client, $"reset-miss-{Guid.NewGuid():N}@example.com", "123456", "newsecret1");
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        var email = $"reset-still-unverified-{Guid.NewGuid():N}@example.com";
        await RegisterAsync(client, email);
        var unverified = await ResetAsync(client, email, "123456", "newsecret1");
        Assert.Equal(HttpStatusCode.BadRequest, unverified.StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "secret1" })).StatusCode);
    }

    private static Task<HttpResponseMessage> RequestPasswordResetAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/auth/forgot", new { email });

    private static Task<HttpResponseMessage> ResetAsync(
        HttpClient client, string email, string code, string newPassword, string? confirm = null) =>
        client.PostAsJsonAsync("/api/auth/reset", new
        {
            email,
            code,
            newPassword,
            newPasswordConfirm = confirm ?? newPassword
        });

    private static async Task<string> RegisterAsync(HttpClient client, string email, string password = "secret1")
    {
        var register = await client.PostAsJsonAsync("/api/auth/register", new { email, password });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        return auth.Token;
    }

    private static async Task<HttpResponseMessage> GetMeAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static IReadOnlyList<EmailMessage> ResetEmails(AuthApiFactory factory) =>
        factory.Emails.Where(e => e.Subject.Contains(ResetSubject)).ToList();

    private static string ResetCodeFrom(AuthApiFactory factory)
    {
        var letter = ResetEmails(factory).Last();
        var match = CodeRegex.Match(letter.HtmlBody);
        Assert.True(match.Success, "Код не найден в теле письма.");
        return match.Groups[1].Value;
    }
}
