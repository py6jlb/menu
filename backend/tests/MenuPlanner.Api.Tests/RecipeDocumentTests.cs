using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Families;
using MenuPlanner.Api.Recipes;

namespace MenuPlanner.Api.Tests;

/// <summary>
/// PDF-документ рецепта: загрузка/замена/удаление, отдача как application/pdf,
/// лимиты и сигнатура, необязательные шаги, анонимный просмотр по ссылке.
/// </summary>
public sealed class RecipeDocumentTests
{
    [Fact]
    public async Task UploadDocument_SetsDocumentUrl_StoresFile_AndServesAsPdf()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var (_, recipe) = await PostAuthorizedAsync<RecipeDto>(client, owner.Token, "/api/recipes", FullRequest());
        Assert.Null(recipe!.DocumentUrl);

        var bytes = TestPdf();
        var (response, dto) = await PutDocumentAsync<RecipeDto>(
            client, owner.Token, recipe.Id, recipe.Revision, bytes, "application/pdf", "recipe.pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(dto);
        Assert.StartsWith("/api/documents/", dto!.DocumentUrl);
        Assert.EndsWith(".pdf", dto.DocumentUrl);

        var storedFile = Path.Combine(factory.DocumentsDir, Path.GetFileName(dto.DocumentUrl!));
        Assert.True(File.Exists(storedFile));

        var getResponse = await client.GetAsync(dto.DocumentUrl!);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal("application/pdf", getResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(bytes, await getResponse.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task UploadDocument_Replacing_DeletesPreviousFile()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var (_, recipe) = await PostAuthorizedAsync<RecipeDto>(client, owner.Token, "/api/recipes", FullRequest());

        var (_, first) = await PutDocumentAsync<RecipeDto>(
            client, owner.Token, recipe!.Id, recipe.Revision, TestPdf("1"), "application/pdf", "a.pdf");
        var firstFile = Path.Combine(factory.DocumentsDir, Path.GetFileName(first!.DocumentUrl!));
        Assert.True(File.Exists(firstFile));

        var (response, second) = await PutDocumentAsync<RecipeDto>(
            client, owner.Token, recipe.Id, first.Revision, TestPdf("2"), "application/pdf", "b.pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(first.DocumentUrl, second!.DocumentUrl);
        Assert.False(File.Exists(firstFile));
        Assert.True(File.Exists(Path.Combine(factory.DocumentsDir, Path.GetFileName(second.DocumentUrl!))));
    }

    [Fact]
    public async Task UploadDocument_NonPdf_ReturnsBadRequest()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var (_, recipe) = await PostAuthorizedAsync<RecipeDto>(client, owner.Token, "/api/recipes", FullRequest());

        var (response, error) = await PutDocumentAsync<RecipeErrorDto>(
            client, owner.Token, recipe!.Id, recipe.Revision,
            Encoding.ASCII.GetBytes("это не pdf"), "application/pdf", "fake.pdf");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("PDF", error!.Error);
        Assert.Empty(Directory.GetFiles(factory.DocumentsDir));
    }

    [Fact]
    public async Task UploadDocument_TooLarge_ReturnsBadRequest()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var (_, recipe) = await PostAuthorizedAsync<RecipeDto>(client, owner.Token, "/api/recipes", FullRequest());

        var bytes = new byte[RecipeCatalog.DocumentMaxBytes + 1];
        var (response, error) = await PutDocumentAsync<RecipeErrorDto>(
            client, owner.Token, recipe!.Id, recipe.Revision, bytes, "application/pdf", "big.pdf");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("МБ", error!.Error);
    }

    [Fact]
    public async Task Recipe_CanBeSavedWithoutSteps()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        var request = FullRequest() with
        {
            Steps = new List<RecipeStepRequest>(),
            Ingredients = new List<RecipeIngredientRequest>()
        };
        var (response, recipe) = await PostAuthorizedAsync<RecipeDto>(client, owner.Token, "/api/recipes", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Empty(recipe!.Steps);
        Assert.Empty(recipe.Ingredients);
    }

    [Fact]
    public async Task DeleteDocument_ClearsUrlAndRemovesFile()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var (_, recipe) = await PostAuthorizedAsync<RecipeDto>(client, owner.Token, "/api/recipes", FullRequest());
        var (_, uploaded) = await PutDocumentAsync<RecipeDto>(
            client, owner.Token, recipe!.Id, recipe.Revision, TestPdf(), "application/pdf", "a.pdf");
        var file = Path.Combine(factory.DocumentsDir, Path.GetFileName(uploaded!.DocumentUrl!));

        var deleteResponse = await DeleteAuthorizedAsync(
            client, owner.Token, $"/api/recipes/{recipe.Id}/document?revision={uploaded.Revision}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.False(File.Exists(file));

        var (_, detail) = await GetAuthorizedAsync<RecipeDto>(client, owner.Token, $"/api/recipes/{recipe.Id}");
        Assert.Null(detail!.DocumentUrl);
    }

    [Fact]
    public async Task DeleteRecipe_RemovesDocumentFile()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var (_, recipe) = await PostAuthorizedAsync<RecipeDto>(client, owner.Token, "/api/recipes", FullRequest());
        var (_, uploaded) = await PutDocumentAsync<RecipeDto>(
            client, owner.Token, recipe!.Id, recipe.Revision, TestPdf(), "application/pdf", "a.pdf");
        var file = Path.Combine(factory.DocumentsDir, Path.GetFileName(uploaded!.DocumentUrl!));

        var deleteResponse = await DeleteAuthorizedAsync(
            client, owner.Token, $"/api/recipes/{recipe.Id}?revision={uploaded.Revision}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task SharedRecipe_ReturnsDocumentUrl_ForAnonymousView()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var (_, recipe) = await PostAuthorizedAsync<RecipeDto>(client, owner.Token, "/api/recipes", FullRequest());
        var (_, uploaded) = await PutDocumentAsync<RecipeDto>(
            client, owner.Token, recipe!.Id, recipe.Revision, TestPdf(), "application/pdf", "a.pdf");

        var (shareResponse, share) = await PostAuthorizedAsync<RecipeShareDto>(
            client, owner.Token, $"/api/recipes/{recipe.Id}/share", null);
        Assert.True(
            shareResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
            $"Неожиданный код создания ссылки: {shareResponse.StatusCode}");

        using var anonymous = factory.CreateClient();
        var (sharedResponse, shared) = await GetAsync<RecipeDto>(anonymous, $"/api/shared/{share!.Token}");

        Assert.Equal(HttpStatusCode.OK, sharedResponse.StatusCode);
        Assert.Equal(uploaded!.DocumentUrl, shared!.DocumentUrl);
    }

    private static byte[] TestPdf(string marker = "1.7") =>
        Encoding.ASCII.GetBytes($"%PDF-{marker}\n1 0 obj\n<<>>\nendobj\ntrailer\n%%EOF");

    private static async Task<(HttpResponseMessage Response, T? Data)> PutDocumentAsync<T>(
        HttpClient client, string token, Guid recipeId, int revision, byte[] bytes, string contentType, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);

        using var request = new HttpRequestMessage(
            HttpMethod.Put, $"/api/recipes/{recipeId}/document?revision={revision}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = content;
        var response = await client.SendAsync(request);
        var data = await ReadJsonAsync<T>(response);
        return (response, data);
    }

    private static RecipeRequest FullRequest() => new(
        Name: "Борщ",
        Description: "Классический борщ",
        CookTimeMinutes: 90,
        Servings: 6,
        Difficulty: 3,
        Calories: 350,
        Tags: new List<string>(),
        Seasonality: new List<string>(),
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

    private static async Task<(HttpResponseMessage Response, T? Data)> GetAsync<T>(
        HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
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

    private static async Task<HttpResponseMessage> DeleteAuthorizedAsync(
        HttpClient client, string token, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}
