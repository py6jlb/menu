using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Families;
using MenuPlanner.Api.Recipes;

namespace MenuPlanner.Api.Tests;

public sealed class SharedRecipeFlowTests
{
    [Fact]
    public async Task Anonymous_SeesLiveFullRecipe_WithPhoto()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var (_, recipe) = await PostAuthorizedAsync<RecipeDto>(
            client, owner.Token, "/api/recipes", FullRequest());
        var (_, uploaded) = await PutPhotoAuthorizedAsync<RecipeDto>(
            client, owner.Token, recipe!.Id, recipe.Revision,
            Encoding.ASCII.GetBytes("png-bytes"), "image/png", "photo.png");
        var (_, share) = await CreateShareAsync(client, owner.Token, recipe.Id);

        var (response, shared) = await GetAsync<RecipeDto>(client, $"/api/shared/{share!.Token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(shared);
        Assert.Equal("Борщ", shared.Name);
        Assert.Equal("Классический борщ", shared.Description);
        Assert.Equal(90, shared.CookTimeMinutes);
        Assert.Equal(6, shared.Servings);
        Assert.Equal(3, shared.Difficulty);
        Assert.Equal(350, shared.Calories);
        Assert.Equal(new[] { "суп", "первое" }, shared.Tags);
        Assert.Equal(new[] { "winter", "autumn" }, shared.Seasonality);
        Assert.Empty(shared.Diet);
        Assert.Equal(new[] { "Сварить бульон.", "Добавить свёклу." }, shared.Steps);
        Assert.Equal(2, shared.Ingredients.Count);
        Assert.Equal("Свёкла", shared.Ingredients[0].Name);
        Assert.Equal(2m, shared.Ingredients[0].Amount);
        Assert.Equal("pcs", shared.Ingredients[0].Unit);
        Assert.Equal("Соль", shared.Ingredients[1].Name);
        Assert.Equal("по вкусу", shared.Ingredients[1].Note);
        Assert.Equal(uploaded!.PhotoUrl, shared.PhotoUrl);
        Assert.NotNull(shared.PhotoUrl);
    }

    [Fact]
    public async Task SourceEdit_IsVisible_OnReopen()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var (_, recipe) = await PostAuthorizedAsync<RecipeDto>(
            client, owner.Token, "/api/recipes", FullRequest());
        var (_, share) = await CreateShareAsync(client, owner.Token, recipe!.Id);

        var updateRequest = FullRequest() with
        {
            Name = "Борщ по-домашнему",
            CookTimeMinutes = 120,
            Servings = 8,
            Steps = new List<RecipeStepRequest> { new("Потушить свёклу.") },
            Ingredients = new List<RecipeIngredientRequest> { new("Капуста", 1, "kg", "свежая") },
            Revision = recipe!.Revision
        };
        var (updateResponse, updated) = await PutAuthorizedAsync<RecipeDto>(
            client, owner.Token, $"/api/recipes/{recipe.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var (_, uploaded) = await PutPhotoAuthorizedAsync<RecipeDto>(
            client, owner.Token, recipe.Id, updated!.Revision,
            Encoding.ASCII.GetBytes("new-png-bytes"), "image/png", "new.png");

        var (response, shared) = await GetAsync<RecipeDto>(client, $"/api/shared/{share!.Token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Борщ по-домашнему", shared!.Name);
        Assert.Equal(120, shared.CookTimeMinutes);
        Assert.Equal(8, shared.Servings);
        Assert.Equal(new[] { "Потушить свёклу." }, shared.Steps);
        var ingredient = Assert.Single(shared.Ingredients);
        Assert.Equal("Капуста", ingredient.Name);
        Assert.Equal(1m, ingredient.Amount);
        Assert.Equal("kg", ingredient.Unit);
        Assert.Equal("свежая", ingredient.Note);
        Assert.Equal(uploaded!.PhotoUrl, shared.PhotoUrl);
    }

    [Fact]
    public async Task RevokedToken_ReturnsInvalidLink()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var (_, recipe) = await PostAuthorizedAsync<RecipeDto>(
            client, owner.Token, "/api/recipes", FullRequest());
        var (_, share) = await CreateShareAsync(client, owner.Token, recipe!.Id);
        await DeleteAuthorizedAsync(client, owner.Token, $"/api/recipes/{recipe.Id}/share");

        var (response, error) = await GetAsync<RecipeErrorDto>(client, $"/api/shared/{share!.Token}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Ссылка недействительна.", error!.Error);
    }

    [Fact]
    public async Task UnknownToken_ReturnsInvalidLink_WithoutContent()
    {
        using var client = new ApiFactory().CreateClient();

        var (response, error) = await GetAsync<RecipeErrorDto>(
            client, $"/api/shared/{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Ссылка недействительна.", error!.Error);
    }

    [Fact]
    public async Task DeletedSource_ReturnsInvalidLink()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var (_, recipe) = await PostAuthorizedAsync<RecipeDto>(
            client, owner.Token, "/api/recipes", FullRequest());
        var (_, share) = await CreateShareAsync(client, owner.Token, recipe!.Id);
        await DeleteAuthorizedAsync(client, owner.Token,
            $"/api/recipes/{recipe.Id}?revision={recipe.Revision}");

        var (response, error) = await GetAsync<RecipeErrorDto>(client, $"/api/shared/{share!.Token}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Ссылка недействительна.", error!.Error);
    }

    private static RecipeRequest FullRequest() => new(
        Name: "Борщ",
        Description: "Классический борщ",
        CookTimeMinutes: 90,
        Servings: 6,
        Difficulty: 3,
        Calories: 350,
        Tags: new List<string> { "суп", "первое" },
        Seasonality: new List<string> { "winter", "autumn" },
        Diet: new List<string>(),
        Steps: new List<RecipeStepRequest>
        {
            new("Сварить бульон."),
            new("Добавить свёклу.")
        },
        Ingredients: new List<RecipeIngredientRequest>
        {
            new("Свёкла", 2, "pcs", null),
            new("Соль", 0.5m, "tsp", "по вкусу")
        });

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
        var (response, _) = await PostAuthorizedAsync<FamilyDto>(
            client, token, "/api/families", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static Task<(HttpResponseMessage Response, RecipeShareDto? Data)> CreateShareAsync(
        HttpClient client, string token, Guid recipeId) =>
        PostAuthorizedAsync<RecipeShareDto>(client, token, $"/api/recipes/{recipeId}/share", new { });

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

    private static async Task<(HttpResponseMessage Response, T? Data)> PutAuthorizedAsync<T>(
        HttpClient client, string token, string path, object? body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        var response = await client.SendAsync(request);
        var data = await ReadJsonAsync<T>(response);
        return (response, data);
    }

    private static async Task<(HttpResponseMessage Response, T? Data)> PutPhotoAuthorizedAsync<T>(
        HttpClient client, string token, Guid recipeId, int revision, byte[] bytes, string contentType, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);

        using var request = new HttpRequestMessage(
            HttpMethod.Put, $"/api/recipes/{recipeId}/photo?revision={revision}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = content;
        var response = await client.SendAsync(request);
        var data = await ReadJsonAsync<T>(response);
        return (response, data);
    }

    private static async Task<(HttpResponseMessage Response, T? Data)> GetAsync<T>(
        HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
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
