using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Families;
using MenuPlanner.Api.Recipes;

namespace MenuPlanner.Api.Tests;

/// <summary>
/// Поведение проверяемой ревизии на HTTP-уровне: чтение отдаёт версию,
/// редактирование/удаление/фото/промоушен принимают ожидаемую и отклоняют
/// устаревшее сохранение согласованным конфликтом, не перезаписывая чужое.
/// </summary>
public sealed class RecipeRevisionFlowTests
{
    [Fact]
    public async Task Get_ReturnsRevisionStartingAtOne()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var recipe = await CreateRecipeAsync(client, owner.Token, "Борщ");

        var (response, detail) = await GetAuthorizedAsync<RecipeDto>(
            client, owner.Token, $"/api/recipes/{recipe.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, detail!.Revision);
    }

    [Fact]
    public async Task Put_AdvancesRevision_AndStalePutConflictsWithoutOverwrite()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var recipe = await CreateRecipeAsync(client, owner.Token, "Борщ");

        var (firstResponse, first) = await PutAuthorizedAsync<RecipeDto>(
            client, owner.Token, $"/api/recipes/{recipe.Id}",
            FullRequest() with { Name = "Первая правка", Revision = recipe.Revision });
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(2, first!.Revision);

        // Устаревшая ревизия не перезаписывает более новую версию.
        var (staleResponse, conflict) = await PutAuthorizedAsync<RecipeConflictDto>(
            client, owner.Token, $"/api/recipes/{recipe.Id}",
            FullRequest() with { Name = "Устаревшая правка", Revision = recipe.Revision });
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        Assert.Equal(2, conflict!.Revision);

        var (_, detail) = await GetAuthorizedAsync<RecipeDto>(
            client, owner.Token, $"/api/recipes/{recipe.Id}");
        Assert.Equal("Первая правка", detail!.Name);
        Assert.Equal(2, detail.Revision);
    }

    [Fact]
    public async Task Put_WithoutRevision_ReturnsBadRequest()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var recipe = await CreateRecipeAsync(client, owner.Token, "Борщ");

        var (response, error) = await PutAuthorizedAsync<RecipeErrorDto>(
            client, owner.Token, $"/api/recipes/{recipe.Id}", FullRequest() with { Name = "Без версии" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("версия", error!.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Delete_WithStaleRevision_Conflicts_AndDeletesWithCurrent()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var recipe = await CreateRecipeAsync(client, owner.Token, "Борщ");

        var (_, first) = await PutAuthorizedAsync<RecipeDto>(
            client, owner.Token, $"/api/recipes/{recipe.Id}",
            FullRequest() with { Name = "Правка", Revision = recipe.Revision });

        var stale = await DeleteAuthorizedAsync(
            client, owner.Token, $"/api/recipes/{recipe.Id}?revision={recipe.Revision}");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var ok = await DeleteAuthorizedAsync(
            client, owner.Token, $"/api/recipes/{recipe.Id}?revision={first!.Revision}");
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
    }

    [Fact]
    public async Task Photo_WithStaleRevision_Conflicts()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var recipe = await CreateRecipeAsync(client, owner.Token, "Борщ");

        var (upload, uploaded) = await PutPhotoAsync<RecipeDto>(
            client, owner.Token, recipe.Id, recipe.Revision,
            new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png", "photo.png");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        Assert.Equal(2, uploaded!.Revision);

        var (stale, conflict) = await PutPhotoAsync<RecipeConflictDto>(
            client, owner.Token, recipe.Id, recipe.Revision,
            new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png", "photo.png");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(2, conflict!.Revision);
    }

    [Fact]
    public async Task Copy_AdvancesRevision_AndStaleCopyConflicts()
    {
        using var client = new ApiFactory().CreateClient();
        var recipient = await RegisterAsync(client, "recipient");
        await CreateFamilyAsync(client, recipient.Token, "Семья получателя");
        var wrapper = await ImportWrapperAsync(client, recipient);

        var (staleResponse, conflict) = await PostAuthorizedAsync<RecipeConflictDto>(
            client, recipient.Token,
            $"/api/recipes/{wrapper.WrapperId}/copy?revision={wrapper.Revision + 1}", body: null);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        Assert.Equal(wrapper.Revision, conflict!.Revision);

        var (copyResponse, copied) = await PostAuthorizedAsync<RecipeDto>(
            client, recipient.Token,
            $"/api/recipes/{wrapper.WrapperId}/copy?revision={wrapper.Revision}", body: null);
        Assert.Equal(HttpStatusCode.OK, copyResponse.StatusCode);
        Assert.False(copied!.IsExternal);
        Assert.Equal(wrapper.Revision + 1, copied.Revision);
    }

    [Fact]
    public async Task Copy_WithoutRevision_ReturnsBadRequest()
    {
        using var client = new ApiFactory().CreateClient();
        var recipient = await RegisterAsync(client, "recipient");
        await CreateFamilyAsync(client, recipient.Token, "Семья получателя");
        var wrapper = await ImportWrapperAsync(client, recipient);

        var (response, error) = await PostAuthorizedAsync<RecipeErrorDto>(
            client, recipient.Token, $"/api/recipes/{wrapper.WrapperId}/copy", body: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("версия", error!.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(Guid WrapperId, int Revision)> ImportWrapperAsync(
        HttpClient client, AuthResponse recipient)
    {
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья источника");
        var source = await CreateRecipeAsync(client, owner.Token, "Борщ");
        var (_, share) = await PostAuthorizedAsync<RecipeShareDto>(
            client, owner.Token, $"/api/recipes/{source.Id}/share", body: null);

        var (_, imported) = await PostAuthorizedAsync<RecipeImportResultDto>(
            client, recipient.Token, $"/api/shared/{share!.Token}/import", body: null);
        var (_, wrapper) = await GetAuthorizedAsync<RecipeDto>(
            client, recipient.Token, $"/api/recipes/{imported!.RecipeId}");

        return (imported.RecipeId, wrapper!.Revision);
    }

    private static RecipeRequest FullRequest() => new(
        Name: "Борщ",
        Description: "Классический борщ",
        CookTimeMinutes: 90,
        Servings: 6,
        Difficulty: 3,
        Calories: 350,
        Tags: new List<string> { "суп" },
        Seasonality: new List<string> { "winter" },
        Diet: new List<string>(),
        Steps: new List<RecipeStepRequest> { new("Сварить бульон.") },
        Ingredients: new List<RecipeIngredientRequest> { new("Свёкла", 2, "pcs", null) });

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "secret1" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        return auth!;
    }

    private static async Task CreateFamilyAsync(HttpClient client, string token, string name)
    {
        var (response, _) = await PostAuthorizedAsync<FamilyDto>(client, token, "/api/families", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<RecipeDto> CreateRecipeAsync(HttpClient client, string token, string name)
    {
        var (response, recipe) = await PostAuthorizedAsync<RecipeDto>(
            client, token, "/api/recipes", FullRequest() with { Name = name });
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

    private static async Task<(HttpResponseMessage Response, T? Data)> PutPhotoAsync<T>(
        HttpClient client, string token, Guid recipeId, int revision,
        byte[] bytes, string contentType, string fileName)
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

    private static async Task<(HttpResponseMessage Response, T? Data)> GetAuthorizedAsync<T>(
        HttpClient client, string token, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
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
