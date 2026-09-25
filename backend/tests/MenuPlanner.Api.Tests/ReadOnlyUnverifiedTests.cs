using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;
using MenuPlanner.Api.Auth;

namespace MenuPlanner.Api.Tests;

public sealed class ReadOnlyUnverifiedTests
{
    [Fact]
    public async Task Unverified_CannotCreateFamily_ReturnsForbidden()
    {
        using var factory = new ApiFactory { AutoVerifyEmailsOnRegistration = false };
        using var client = factory.CreateClient();
        var user = await RegisterAsync(client, "unverified");

        var response = await SendAsync(client, HttpMethod.Post, "/api/families", user.Token,
            JsonContent.Create(new { name = "Семья" }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unverified_CannotJoinFamily_ReturnsForbidden()
    {
        using var factory = new ApiFactory { AutoVerifyEmailsOnRegistration = false };
        using var client = factory.CreateClient();
        var user = await RegisterAsync(client, "unverified");

        var response = await SendAsync(client, HttpMethod.Post, "/api/families/join", user.Token,
            JsonContent.Create(new { inviteCode = "ABCD1234" }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unverified_CannotCreateRecipe_ReturnsForbidden()
    {
        using var factory = new ApiFactory { AutoVerifyEmailsOnRegistration = false };
        using var client = factory.CreateClient();
        var user = await RegisterAsync(client, "unverified");

        var response = await SendAsync(client, HttpMethod.Post, "/api/recipes", user.Token,
            JsonContent.Create(new { name = "Борщ" }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unverified_CannotUpdateOrDeleteRecipe_ReturnsForbidden()
    {
        using var factory = new ApiFactory { AutoVerifyEmailsOnRegistration = false };
        using var client = factory.CreateClient();
        var user = await RegisterAsync(client, "unverified");
        var recipeId = Guid.NewGuid();

        var update = await SendAsync(client, HttpMethod.Put, $"/api/recipes/{recipeId}", user.Token,
            JsonContent.Create(new { name = "Борщ" }));
        var delete = await SendAsync(client, HttpMethod.Delete, $"/api/recipes/{recipeId}", user.Token, null);

        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact]
    public async Task Unverified_CannotWriteRecipePhoto_ReturnsForbidden()
    {
        using var factory = new ApiFactory { AutoVerifyEmailsOnRegistration = false };
        using var client = factory.CreateClient();
        var user = await RegisterAsync(client, "unverified");
        var recipeId = Guid.NewGuid();

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", "a.png");

        var upload = await SendAsync(client, HttpMethod.Put, $"/api/recipes/{recipeId}/photo", user.Token, content);
        var delete = await SendAsync(client, HttpMethod.Delete, $"/api/recipes/{recipeId}/photo", user.Token, null);

        Assert.Equal(HttpStatusCode.Forbidden, upload.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact]
    public async Task Unverified_CannotChangePlan_ReturnsForbidden()
    {
        using var factory = new ApiFactory { AutoVerifyEmailsOnRegistration = false };
        using var client = factory.CreateClient();
        var user = await RegisterAsync(client, "unverified");
        const string weekStart = "2026-09-07";

        var save = await SendAsync(client, HttpMethod.Put, $"/api/plans/week/{weekStart}", user.Token,
            JsonContent.Create(new { entries = Array.Empty<object>() }));
        var delete = await SendAsync(client, HttpMethod.Delete, $"/api/plans/week/{weekStart}", user.Token, null);

        Assert.Equal(HttpStatusCode.Forbidden, save.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact]
    public async Task Unverified_CanReadData()
    {
        using var factory = new ApiFactory { AutoVerifyEmailsOnRegistration = false };
        using var client = factory.CreateClient();
        var user = await RegisterAsync(client, "unverified");

        var recipes = await SendAsync(client, HttpMethod.Get, "/api/recipes", user.Token, null);
        var plan = await SendAsync(client, HttpMethod.Get, "/api/plans/week/2026-09-07", user.Token, null);
        var shopping = await SendAsync(client,
            HttpMethod.Get, "/api/shopping-list?weekStart=2026-09-07", user.Token, null);
        var settings = await SendAsync(client, HttpMethod.Get, "/api/settings", user.Token, null);
        var family = await SendAsync(client, HttpMethod.Get, "/api/families/my", user.Token, null);

        Assert.Equal(HttpStatusCode.OK, recipes.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, plan.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, shopping.StatusCode);
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, family.StatusCode);
    }

    [Fact]
    public async Task Unverified_CanUseVerificationEndpoints()
    {
        using var factory = new ApiFactory { AutoVerifyEmailsOnRegistration = false };
        using var client = factory.CreateClient();
        var user = await RegisterAsync(client, "unverified");

        var verify = await SendAsync(client, HttpMethod.Post, "/api/auth/verify", user.Token,
            JsonContent.Create(new { code = "000000" }));
        var resend = await SendAsync(client, HttpMethod.Post, "/api/auth/verify/resend", user.Token, null);

        Assert.Equal(HttpStatusCode.BadRequest, verify.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, resend.StatusCode);
    }

    [Fact]
    public async Task Verified_CanCreateFamily()
    {
        using var factory = new ApiFactory { AutoVerifyEmailsOnRegistration = false };
        using var client = factory.CreateClient();
        var user = await RegisterAsync(client, "verified");
        await factory.VerifyUserAsync(user.User.Email);

        var response = await SendAsync(client, HttpMethod.Post, "/api/families", user.Token,
            JsonContent.Create(new { name = "Семья" }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "secret1" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        Assert.False(auth.User.IsEmailVerified);
        return auth;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string path, string token, HttpContent? content)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (content is not null)
            request.Content = content;
        return await client.SendAsync(request);
    }
}
