using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;
using MenuPlanner.Api.Auth;

namespace MenuPlanner.Api.Tests;

public sealed class AuthFlowTests
{
    [Fact]
    public async Task Register_Login_ThenAccessMe_WithToken_ReturnsUser()
    {
        using var client = new ApiFactory().CreateClient();
        var email = $"roundtrip-{Guid.NewGuid():N}@example.com";
        const string password = "secret1";

        var register = await client.PostAsJsonAsync("/api/auth/register", new { email, password });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var registered = await register.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(registered);
        Assert.Equal(email, registered.User.Email);
        Assert.False(string.IsNullOrWhiteSpace(registered.Token));

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loggedIn = await login.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(loggedIn);
        Assert.Equal(email, loggedIn.User.Email);
        Assert.False(string.IsNullOrWhiteSpace(loggedIn.Token));

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loggedIn.Token);
        using var me = await client.SendAsync(meRequest);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var meUser = await me.Content.ReadFromJsonAsync<UserDto>();
        Assert.NotNull(meUser);
        Assert.Equal(email, meUser.Email);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_ReturnsConflict()
    {
        using var client = new ApiFactory().CreateClient();
        var email = $"duplicate-{Guid.NewGuid():N}@example.com";

        var first = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "secret1" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "secret1" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Registration_AlwaysAssignsUserRole_EvenInEmptyDatabase()
    {
        using var client = new ApiFactory().CreateClient();

        // Даже первый публично зарегистрированный пользователь в пустой БД —
        // не Администратор: системная роль выдаётся только закрытым bootstrap.
        var first = await RegisterAsync(client, $"first-{Guid.NewGuid():N}@example.com");
        Assert.Equal("User", first.User.Role);

        var second = await RegisterAsync(client, $"second-{Guid.NewGuid():N}@example.com");
        Assert.Equal("User", second.User.Role);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        using var client = new ApiFactory().CreateClient();
        var email = $"wrongpw-{Guid.NewGuid():N}@example.com";

        await RegisterAsync(client, email);

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Me_WithoutToken_ReturnsUnauthorized()
    {
        using var client = new ApiFactory().CreateClient();

        using var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "secret1" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        return auth;
    }
}
