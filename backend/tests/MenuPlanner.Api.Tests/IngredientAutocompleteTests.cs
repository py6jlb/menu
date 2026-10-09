using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Families;
using MenuPlanner.Api.Ingredients;
using MenuPlanner.Api.Recipes;

namespace MenuPlanner.Api.Tests;

public sealed class IngredientAutocompleteTests
{
    [Fact]
    public async Task Autocomplete_ReturnsFamilyScopedMatches_PrefixCaseInsensitive()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        await CreateRecipeAsync(client, owner.Token, new List<RecipeIngredientRequest>
        {
            new("Помидор", 2, "pcs", null),
            new("Лук", 1, "pcs", null),
            new("Чеснок", 2, "pcs", null)
        });
        await CreateRecipeAsync(client, owner.Token, new List<RecipeIngredientRequest>
        {
            new("Помидор", 1, "kg", null),
            new("ПомидорЧерри", 0.2m, "kg", null)
        });

        var (response, data) = await GetAuthorizedAsync<IngredientAutocompleteDto>(
            client, owner.Token, "/api/ingredients/autocomplete?q=%D0%BF%D0%BE");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(data);
        Assert.Contains(data.Items, i => i.Name == "Помидор");
        Assert.Contains(data.Items, i => i.Name == "ПомидорЧерри");
        Assert.DoesNotContain(data.Items, i => i.Name == "Лук");
        Assert.DoesNotContain(data.Items, i => i.Name == "Чеснок");
    }

    [Fact]
    public async Task Autocomplete_IsCaseInsensitive_AndNormalizesWhitespace()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        await CreateRecipeAsync(client, owner.Token, new List<RecipeIngredientRequest>
        {
            new("Помидор", 1, "pcs", null)
        });

        var (lowerResponse, lowerData) = await GetAuthorizedAsync<IngredientAutocompleteDto>(
            client, owner.Token, "/api/ingredients/autocomplete?q=%D0%BF%D0%BE");
        Assert.Equal(HttpStatusCode.OK, lowerResponse.StatusCode);
        Assert.Contains(lowerData!.Items, i => i.Name == "Помидор");

        var (upperResponse, upperData) = await GetAuthorizedAsync<IngredientAutocompleteDto>(
            client, owner.Token, "/api/ingredients/autocomplete?q=%D0%9F%D0%9E");
        Assert.Equal(HttpStatusCode.OK, upperResponse.StatusCode);
        Assert.Contains(upperData!.Items, i => i.Name == "Помидор");
    }

    [Fact]
    public async Task Autocomplete_BlankQuery_ReturnsMostUsedNames()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        await CreateRecipeAsync(client, owner.Token, new List<RecipeIngredientRequest>
        {
            new("Чеснок", 1, "pcs", null),
            new("Лук", 1, "pcs", null)
        });
        await CreateRecipeAsync(client, owner.Token, new List<RecipeIngredientRequest>
        {
            new("Чеснок", 2, "pcs", null),
            new("Помидор", 3, "pcs", null)
        });

        var (response, data) = await GetAuthorizedAsync<IngredientAutocompleteDto>(
            client, owner.Token, "/api/ingredients/autocomplete?q=");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(data);
        Assert.Equal(3, data.Items.Count);
        Assert.Equal("Чеснок", data.Items[0].Name);
        Assert.Contains(data.Items, i => i.Name == "Лук");
        Assert.Contains(data.Items, i => i.Name == "Помидор");
    }

    [Fact]
    public async Task Autocomplete_DoesNotLeakOtherFamilyNames()
    {
        using var client = new ApiFactory().CreateClient();
        var first = await RegisterAsync(client, "first");
        var second = await RegisterAsync(client, "second");
        await CreateFamilyAsync(client, first.Token, "Семья первая");
        await CreateFamilyAsync(client, second.Token, "Семья вторая");

        await CreateRecipeAsync(client, first.Token, new List<RecipeIngredientRequest>
        {
            new("Помидор", 2, "pcs", null),
            new("Лук", 1, "pcs", null)
        });
        await CreateRecipeAsync(client, second.Token, new List<RecipeIngredientRequest>
        {
            new("Морковь", 3, "pcs", null)
        });

        var (secondResponse, secondData) = await GetAuthorizedAsync<IngredientAutocompleteDto>(
            client, second.Token, "/api/ingredients/autocomplete?q=");
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.NotNull(secondData);
        Assert.Contains(secondData.Items, i => i.Name == "Морковь");
        Assert.DoesNotContain(secondData.Items, i => i.Name == "Помидор");
        Assert.DoesNotContain(secondData.Items, i => i.Name == "Лук");

        var (firstResponse, firstData) = await GetAuthorizedAsync<IngredientAutocompleteDto>(
            client, first.Token, "/api/ingredients/autocomplete?q=%D0%BF%D0%BE");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Contains(firstData!.Items, i => i.Name == "Помидор");
        Assert.DoesNotContain(firstData.Items, i => i.Name == "Морковь");
    }

    [Fact]
    public async Task Autocomplete_NoFamily_ReturnsEmpty()
    {
        using var client = new ApiFactory().CreateClient();
        var user = await RegisterAsync(client, "lonely");

        var (response, data) = await GetAuthorizedAsync<IngredientAutocompleteDto>(
            client, user.Token, "/api/ingredients/autocomplete?q=");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(data);
        Assert.Empty(data.Items);
    }

    [Fact]
    public async Task Autocomplete_WithoutToken_ReturnsUnauthorized()
    {
        using var client = new ApiFactory().CreateClient();
        var response = await client.GetAsync("/api/ingredients/autocomplete?q=");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task CreateRecipeAsync(
        HttpClient client, string token, List<RecipeIngredientRequest> ingredients)
    {
        var request = new RecipeRequest(
            Name: "Блюдо",
            Description: null,
            CookTimeMinutes: 30,
            Servings: 4,
            Difficulty: 2,
            Calories: null,
            Tags: new List<string>(),
            Seasonality: new List<string>(),
            Diet: new List<string>(),
            Steps: new List<RecipeStepRequest> { new("Шаг.") },
            Ingredients: ingredients);

        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/recipes");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Content = JsonContent.Create(request);
        var response = await client.SendAsync(message);
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
        return auth;
    }

    private static async Task CreateFamilyAsync(HttpClient client, string token, string name)
    {
        var (response, _) = await PostAuthorizedAsync<FamilyDto>(client, token, "/api/families", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
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
