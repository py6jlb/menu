using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Data;

namespace MenuPlanner.Api.Tests;

public sealed class AdminUnlockTests
{
    private static readonly Regex CodeRegex = new(@"class=""code"">(\d{6})<", RegexOptions.Compiled);

    [Fact]
    public async Task Unlock_ByAdmin_ResetsLockAndAttempts_SoUserCanVerify()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        var admin = await RegisterAsync(client, "admin");
        var target = await RegisterAsync(client, "locked");
        var code = CodeFrom(factory, target.User.Email);

        for (var i = 0; i < 4; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await VerifyAsync(client, target.Token, "000000")).StatusCode);
        Assert.Equal(HttpStatusCode.Locked, (await VerifyAsync(client, target.Token, "000000")).StatusCode);

        var unlock = await UnlockAsync(client, admin.Token, target.User.Email);
        Assert.Equal(HttpStatusCode.OK, unlock.StatusCode);

        var user = await GetUserAsync(factory, target.User.Email);
        Assert.Equal(0, user.VerificationAttempts);
        Assert.Null(user.LockedUntil);

        Assert.Equal(HttpStatusCode.BadRequest, (await VerifyAsync(client, target.Token, "000000")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await VerifyAsync(client, target.Token, code)).StatusCode);
    }

    [Fact]
    public async Task Unlock_ByNonAdmin_ReturnsForbidden()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        await RegisterAsync(client, "admin");
        var target = await RegisterAsync(client, "locked");

        for (var i = 0; i < 5; i++)
            await VerifyAsync(client, target.Token, "000000");

        var unlock = await UnlockAsync(client, target.Token, target.User.Email);
        Assert.Equal(HttpStatusCode.Forbidden, unlock.StatusCode);

        var user = await GetUserAsync(factory, target.User.Email);
        Assert.NotNull(user.LockedUntil);
    }

    [Fact]
    public async Task Unlock_WithoutToken_ReturnsUnauthorized()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        var target = await RegisterAsync(client, "locked");

        var response = await client.PostAsJsonAsync("/api/admin/unlock", new { email = target.User.Email });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unlock_ForUnknownEmail_ReturnsNotFound()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        var admin = await RegisterAsync(client, "admin");

        var unlock = await UnlockAsync(client, admin.Token, $"missing-{Guid.NewGuid():N}@example.com");

        Assert.Equal(HttpStatusCode.NotFound, unlock.StatusCode);
    }

    [Fact]
    public async Task Unlock_IsRateLimited_ToTenPerMinute_PerAdmin()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        var admin = await RegisterAsync(client, "admin");
        var target = await RegisterAsync(client, "locked");

        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await UnlockAsync(client, admin.Token, target.User.Email)).StatusCode);

        var eleventh = await UnlockAsync(client, admin.Token, target.User.Email);
        Assert.Equal(HttpStatusCode.TooManyRequests, eleventh.StatusCode);
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "secret1" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        return auth;
    }

    private static Task<HttpResponseMessage> VerifyAsync(HttpClient client, string token, string code) =>
        PostAuthorizedAsync(client, token, "/api/auth/verify", new { code });

    private static Task<HttpResponseMessage> UnlockAsync(HttpClient client, string token, string email) =>
        PostAuthorizedAsync(client, token, "/api/admin/unlock", new { email });

    private static async Task<HttpResponseMessage> PostAuthorizedAsync(
        HttpClient client, string token, string url, object? body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(body ?? new { });
        return await client.SendAsync(request);
    }

    private static string CodeFrom(AuthApiFactory factory, string email)
    {
        var letter = factory.Emails.Last(e => e.To == email);
        var match = CodeRegex.Match(letter.HtmlBody);
        Assert.True(match.Success, "Код не найден в теле письма.");
        return match.Groups[1].Value;
    }

    private static async Task<MenuPlanner.Api.Domain.User> GetUserAsync(AuthApiFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.SingleAsync(u => u.Email == email);
    }
}
