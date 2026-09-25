using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Families;
using MenuPlanner.Api.Recipes;

namespace MenuPlanner.Api.Tests;

public sealed class RecipeShareFlowTests
{
    [Fact]
    public async Task Share_FirstCallCreatesLink_SecondReturnsSame()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var recipe = await CreateRecipeAsync(client, owner.Token, "Борщ");

        var (firstResponse, first) = await GetAuthorizedAsync<RecipeShareDto>(
            client, owner.Token, $"/api/recipes/{recipe.Id}/share");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.NotNull(first);
        Assert.False(first.Revoked);
        Assert.False(string.IsNullOrWhiteSpace(first.Token));
        Assert.Equal($"/r/{first.Token}", first.Path);
        Assert.Equal($"https://menu.example.com/r/{first.Token}", first.Url);

        var (secondResponse, second) = await GetAuthorizedAsync<RecipeShareDto>(
            client, owner.Token, $"/api/recipes/{recipe.Id}/share");
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(first.Token, second!.Token);
        Assert.Equal(first.Url, second.Url);

        Assert.Equal(1, await CountSharesAsync(factory, recipe.Id));
    }

    [Fact]
    public async Task Share_ByFamilyMember_CreatesLink()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        var member = await RegisterAsync(client, "member");
        var family = await CreateFamilyAsync(client, owner.Token, "Семья");
        await JoinFamilyAsync(client, member.Token, family.InviteCode);
        var recipe = await CreateRecipeAsync(client, owner.Token, "Борщ");

        var (response, share) = await GetAuthorizedAsync<RecipeShareDto>(
            client, member.Token, $"/api/recipes/{recipe.Id}/share");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(share);
        Assert.False(string.IsNullOrWhiteSpace(share.Token));
    }

    [Fact]
    public async Task Share_FromAnotherFamily_ReturnsNotFound()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var first = await RegisterAsync(client, "first");
        var second = await RegisterAsync(client, "second");
        await CreateFamilyAsync(client, first.Token, "Первая");
        await CreateFamilyAsync(client, second.Token, "Вторая");
        var recipe = await CreateRecipeAsync(client, first.Token, "Борщ");

        var (response, _) = await GetAuthorizedAsync<RecipeShareDto>(
            client, second.Token, $"/api/recipes/{recipe.Id}/share");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Share_WithoutFamily_ReturnsNotFound()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var lonely = await RegisterAsync(client, "lonely");
        var recipeId = Guid.NewGuid();

        var (response, _) = await GetAuthorizedAsync<RecipeShareDto>(
            client, lonely.Token, $"/api/recipes/{recipeId}/share");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Share_WithoutToken_ReturnsUnauthorized()
    {
        using var client = new ApiFactory().CreateClient();
        var recipeId = Guid.NewGuid();

        var get = await client.GetAsync($"/api/recipes/{recipeId}/share");
        var revoke = await client.DeleteAsync($"/api/recipes/{recipeId}/share");
        var regenerate = await client.PostAsJsonAsync($"/api/recipes/{recipeId}/share/regenerate", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, regenerate.StatusCode);
    }

    [Fact]
    public async Task Revoke_ByOwner_MarksLinkDead()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var recipe = await CreateRecipeAsync(client, owner.Token, "Борщ");
        var (_, share) = await GetAuthorizedAsync<RecipeShareDto>(
            client, owner.Token, $"/api/recipes/{recipe.Id}/share");

        var (response, revoked) = await DeleteAuthorizedAsync<RecipeShareDto>(
            client, owner.Token, $"/api/recipes/{recipe.Id}/share");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(revoked);
        Assert.True(revoked.Revoked);
        Assert.NotNull(revoked.RevokedAt);
        Assert.Equal(share!.Token, revoked.Token);

        var (afterResponse, after) = await GetAuthorizedAsync<RecipeShareDto>(
            client, owner.Token, $"/api/recipes/{recipe.Id}/share");
        Assert.Equal(HttpStatusCode.OK, afterResponse.StatusCode);
        Assert.True(after!.Revoked);
        Assert.Equal(share.Token, after.Token);
    }

    [Fact]
    public async Task Revoke_WithoutShare_ReturnsNotFound()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var recipe = await CreateRecipeAsync(client, owner.Token, "Борщ");

        var response = await DeleteAuthorizedAsync(
            client, owner.Token, $"/api/recipes/{recipe.Id}/share");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Member_CannotRevokeOrRegenerate_ReturnsForbidden()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        var member = await RegisterAsync(client, "member");
        var family = await CreateFamilyAsync(client, owner.Token, "Семья");
        await JoinFamilyAsync(client, member.Token, family.InviteCode);
        var recipe = await CreateRecipeAsync(client, owner.Token, "Борщ");
        await GetAuthorizedAsync<RecipeShareDto>(client, owner.Token, $"/api/recipes/{recipe.Id}/share");

        var revoke = await DeleteAuthorizedAsync(
            client, member.Token, $"/api/recipes/{recipe.Id}/share");
        var regenerate = await PostAuthorizedAsync<RecipeShareDto>(
            client, member.Token, $"/api/recipes/{recipe.Id}/share/regenerate", new { });

        Assert.Equal(HttpStatusCode.Forbidden, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, regenerate.Response.StatusCode);
    }

    [Fact]
    public async Task Regenerate_ByOwner_ReplacesToken()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var recipe = await CreateRecipeAsync(client, owner.Token, "Борщ");
        var (_, original) = await GetAuthorizedAsync<RecipeShareDto>(
            client, owner.Token, $"/api/recipes/{recipe.Id}/share");

        var revoke = await DeleteAuthorizedAsync(
            client, owner.Token, $"/api/recipes/{recipe.Id}/share");
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);

        var (response, regenerated) = await PostAuthorizedAsync<RecipeShareDto>(
            client, owner.Token, $"/api/recipes/{recipe.Id}/share/regenerate", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(regenerated);
        Assert.NotEqual(original!.Token, regenerated.Token);
        Assert.False(regenerated.Revoked);
        Assert.Null(regenerated.RevokedAt);
        Assert.Equal($"/r/{regenerated.Token}", regenerated.Path);

        Assert.Equal(1, await CountSharesAsync(factory, recipe.Id));
        Assert.False(await ShareTokenExistsAsync(factory, original.Token));
        Assert.True(await ShareTokenExistsAsync(factory, regenerated.Token));
    }

    private static async Task<int> CountSharesAsync(ApiFactory factory, Guid recipeId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.RecipeShares.CountAsync(s => s.RecipeId == recipeId);
    }

    private static async Task<bool> ShareTokenExistsAsync(ApiFactory factory, string token)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.RecipeShares.AnyAsync(s => s.Token == token);
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

    private static async Task<FamilyDto> CreateFamilyAsync(HttpClient client, string token, string name)
    {
        var (response, family) = await PostAuthorizedAsync<FamilyDto>(
            client, token, "/api/families", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return family!;
    }

    private static async Task JoinFamilyAsync(HttpClient client, string token, string inviteCode)
    {
        var (response, _) = await PostAuthorizedAsync<FamilyDto>(
            client, token, "/api/families/join", new { inviteCode });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<RecipeDto> CreateRecipeAsync(HttpClient client, string token, string name)
    {
        var body = new RecipeRequest(
            Name: name,
            Description: null,
            CookTimeMinutes: 30,
            Servings: 2,
            Difficulty: 1,
            Calories: null,
            Tags: new List<string>(),
            Seasonality: new List<string>(),
            Diet: new List<string>(),
            Steps: new List<RecipeStepRequest> { new("Смешать.") },
            Ingredients: new List<RecipeIngredientRequest> { new("Соль", 1, "tsp", null) });

        var (response, recipe) = await PostAuthorizedAsync<RecipeDto>(client, token, "/api/recipes", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return recipe!;
    }

    private static async Task<(HttpResponseMessage Response, T? Data)> PostAuthorizedAsync<T>(
        HttpClient client, string token, string path, object? body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        var response = await client.SendAsync(request);
        var data = await ReadJsonAsync<T>(response);
        return (response, data);
    }

    private static async Task<(HttpResponseMessage Response, T? Data)> GetAuthorizedAsync<T>(
        HttpClient client, string token, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        var data = await ReadJsonAsync<T>(response);
        return (response, data);
    }

    private static async Task<(HttpResponseMessage Response, T? Data)> DeleteAuthorizedAsync<T>(
        HttpClient client, string token, string path)
    {
        var response = await DeleteAuthorizedAsync(client, token, path);
        var data = await ReadJsonAsync<T>(response);
        return (response, data);
    }

    private static async Task<HttpResponseMessage> DeleteAuthorizedAsync(
        HttpClient client, string token, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>();
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
