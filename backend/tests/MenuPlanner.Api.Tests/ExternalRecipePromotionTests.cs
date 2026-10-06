using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Families;
using MenuPlanner.Api.Plans;
using MenuPlanner.Api.Recipes;

namespace MenuPlanner.Api.Tests;

public sealed class ExternalRecipePromotionTests
{
    private const string Monday = "2026-09-07";

    [Fact]
    public async Task Copy_PromotesWrapperInPlace_KeepsContentAndOriginLabel()
    {
        using var client = new ApiFactory().CreateClient();
        var (owner, recipient, imported) = await ImportAsync(client);

        var (response, copied) = await PostAuthorizedAsync<RecipeDto>(
            client, recipient.Token, $"/api/recipes/{imported.WrapperId}/copy", body: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(copied);
        Assert.Equal(imported.WrapperId, copied!.Id);
        Assert.False(copied.IsExternal);
        Assert.Null(copied.State);
        Assert.Equal("Борщ", copied.Name);
        Assert.Equal("Классический борщ", copied.Description);
        Assert.Equal(new[] { "Сварить бульон.", "Добавить свёклу." }, copied.Steps);
        Assert.Equal(2, copied.Ingredients.Count);
        Assert.NotNull(copied.CopiedFromFamilyName);
        Assert.Equal("Семья источника", copied.CopiedFromFamilyName);

        var (_, own) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(
            client, recipient.Token, "/api/recipes?scope=own");
        var ownItem = Assert.Single(own!);
        Assert.Equal(imported.WrapperId, ownItem.Id);
        Assert.Equal("Семья источника", ownItem.CopiedFromFamilyName);

        var (_, external) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(
            client, recipient.Token, "/api/recipes?scope=external");
        Assert.Empty(external!);
    }

    [Fact]
    public async Task Copy_SurvivesSourceDeletionAndSourceEdits()
    {
        using var client = new ApiFactory().CreateClient();
        var (owner, recipient, imported) = await ImportAsync(client);

        await PostAuthorizedAsync<RecipeDto>(
            client, recipient.Token, $"/api/recipes/{imported.WrapperId}/copy", body: null);

        await PutAuthorizedAsync<RecipeDto>(
            client, owner.Token, $"/api/recipes/{imported.SourceId}",
            FullRequest() with { Name = "Борщ по-домашнему" });

        var (_, beforeDelete) = await GetAuthorizedAsync<RecipeDto>(
            client, recipient.Token, $"/api/recipes/{imported.WrapperId}");
        Assert.Equal("Борщ", beforeDelete!.Name);

        await DeleteAuthorizedAsync(client, owner.Token, $"/api/recipes/{imported.SourceId}");

        var (detailResponse, detail) = await GetAuthorizedAsync<RecipeDto>(
            client, recipient.Token, $"/api/recipes/{imported.WrapperId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.Equal("Борщ", detail!.Name);
        Assert.False(detail.IsExternal);
        Assert.Null(detail.State);
        Assert.Equal(new[] { "Сварить бульон.", "Добавить свёклу." }, detail.Steps);
        Assert.Equal("Семья источника", detail.CopiedFromFamilyName);
    }

    [Fact]
    public async Task Copy_IsEditableAndDeletable_LikeOrdinaryRecipe()
    {
        using var client = new ApiFactory().CreateClient();
        var (_, recipient, imported) = await ImportAsync(client);

        await PostAuthorizedAsync<RecipeDto>(
            client, recipient.Token, $"/api/recipes/{imported.WrapperId}/copy", body: null);

        var (updateResponse, updated) = await PutAuthorizedAsync<RecipeDto>(
            client, recipient.Token, $"/api/recipes/{imported.WrapperId}",
            FullRequest() with { Name = "Мой борщ" });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal("Мой борщ", updated!.Name);
        Assert.Null(updated.CopiedFromFamilyName);
        Assert.False(updated.IsExternal);

        var (_, detail) = await GetAuthorizedAsync<RecipeDto>(
            client, recipient.Token, $"/api/recipes/{imported.WrapperId}");
        Assert.Null(detail!.CopiedFromFamilyName);

        var deleteResponse = await DeleteAuthorizedAsync(
            client, recipient.Token, $"/api/recipes/{imported.WrapperId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var (listResponse, list) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(
            client, recipient.Token, "/api/recipes");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Empty(list!);
    }

    [Fact]
    public async Task Copy_KeepsPlanEntriesPointingAtSameRecipe()
    {
        using var client = new ApiFactory().CreateClient();
        var (owner, recipient, imported) = await ImportAsync(client);

        var save = await PutAuthorizedAsync<WeekPlanDto>(client, recipient.Token,
            $"/api/plans/week/{Monday}", new SaveWeekPlanRequest(new[]
            {
                new PlanEntryRequest(0, "dinner", imported.WrapperId, 3)
            }));
        Assert.Equal(HttpStatusCode.OK, save.Response.StatusCode);

        var (copyResponse, _) = await PostAuthorizedAsync<RecipeDto>(
            client, recipient.Token, $"/api/recipes/{imported.WrapperId}/copy", body: null);
        Assert.Equal(HttpStatusCode.OK, copyResponse.StatusCode);

        var (_, plan) = await GetAuthorizedAsync<WeekPlanDto>(
            client, recipient.Token, $"/api/plans/week/{Monday}");
        var entry = Assert.Single(plan!.Entries);
        Assert.Equal(imported.WrapperId, entry.RecipeId);
        Assert.Equal("Борщ", entry.RecipeName);
        Assert.Equal(3, entry.Portions);
        Assert.Null(entry.State);

        await DeleteAuthorizedAsync(client, owner.Token, $"/api/recipes/{imported.SourceId}");

        var (_, planAfter) = await GetAuthorizedAsync<WeekPlanDto>(
            client, recipient.Token, $"/api/plans/week/{Monday}");
        var entryAfter = Assert.Single(planAfter!.Entries);
        Assert.Equal(imported.WrapperId, entryAfter.RecipeId);
        Assert.Equal("Борщ", entryAfter.RecipeName);
        Assert.Null(entryAfter.State);
    }

    [Fact]
    public async Task Copy_CopiesPhotoFileToOwnName()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var (owner, recipient, imported) = await ImportAsync(client);

        var bytes = Encoding.ASCII.GetBytes("source-photo-bytes");
        var (photoResponse, source) = await PutPhotoAuthorizedAsync<RecipeDto>(
            client, owner.Token, imported.SourceId, bytes, "image/png", "photo.png");
        Assert.Equal(HttpStatusCode.OK, photoResponse.StatusCode);
        Assert.NotNull(source!.PhotoUrl);

        var (copyResponse, copied) = await PostAuthorizedAsync<RecipeDto>(
            client, recipient.Token, $"/api/recipes/{imported.WrapperId}/copy", body: null);
        Assert.Equal(HttpStatusCode.OK, copyResponse.StatusCode);
        Assert.NotNull(copied!.PhotoUrl);
        Assert.NotEqual(source.PhotoUrl, copied.PhotoUrl);

        var sourceFile = Path.Combine(factory.PhotosDir, Path.GetFileName(source.PhotoUrl!));
        var copiedFile = Path.Combine(factory.PhotosDir, Path.GetFileName(copied.PhotoUrl!));
        Assert.True(File.Exists(sourceFile));
        Assert.True(File.Exists(copiedFile));
        Assert.Equal(await File.ReadAllBytesAsync(sourceFile), await File.ReadAllBytesAsync(copiedFile));

        await DeleteAuthorizedAsync(client, owner.Token, $"/api/recipes/{imported.SourceId}");
        Assert.False(File.Exists(sourceFile));

        var served = await client.GetAsync(copied.PhotoUrl!);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal(bytes, await served.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Copy_BrokenSource_ReturnsBadRequest()
    {
        using var client = new ApiFactory().CreateClient();
        var (owner, recipient, imported) = await ImportAsync(client);

        await DeleteAuthorizedAsync(client, owner.Token, $"/api/recipes/{imported.SourceId}");

        var (response, error) = await PostAuthorizedAsync<RecipeErrorDto>(
            client, recipient.Token, $"/api/recipes/{imported.WrapperId}/copy", body: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(error);
        Assert.False(string.IsNullOrWhiteSpace(error!.Error));
    }

    [Fact]
    public async Task Copy_OnOwnRecipe_ReturnsBadRequest()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var own = await CreateRecipeAsync(client, owner.Token, "Свой суп");

        var (response, error) = await PostAuthorizedAsync<RecipeErrorDto>(
            client, owner.Token, $"/api/recipes/{own.Id}/copy", body: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Это не внешний рецепт.", error!.Error);
    }

    [Fact]
    public async Task Copy_Twice_ReturnsBadRequest()
    {
        using var client = new ApiFactory().CreateClient();
        var (_, recipient, imported) = await ImportAsync(client);

        await PostAuthorizedAsync<RecipeDto>(
            client, recipient.Token, $"/api/recipes/{imported.WrapperId}/copy", body: null);

        var (response, error) = await PostAuthorizedAsync<RecipeErrorDto>(
            client, recipient.Token, $"/api/recipes/{imported.WrapperId}/copy", body: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Это не внешний рецепт.", error!.Error);
    }

    private static async Task<(AuthResponse Owner, AuthResponse Recipient, Imported Imported)> ImportAsync(
        HttpClient client)
    {
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья источника");
        var source = await CreateRecipeAsync(client, owner.Token, "Борщ");
        var share = await ShareAsync(client, owner.Token, source.Id);

        var recipient = await RegisterAsync(client, "recipient");
        await CreateFamilyAsync(client, recipient.Token, "Семья получателя");
        var (_, imported) = await PostAuthorizedAsync<RecipeImportResultDto>(
            client, recipient.Token, $"/api/shared/{share.Token}/import", body: null);

        return (owner, recipient, new Imported(imported!.RecipeId, source.Id));
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
        Steps: new List<RecipeStepRequest> { new("Сварить бульон."), new("Добавить свёклу.") },
        Ingredients: new List<RecipeIngredientRequest>
        {
            new("Свёкла", 2, "pcs", null),
            new("Соль", 0.5m, "tsp", null)
        });

    private static async Task<RecipeShareDto> ShareAsync(HttpClient client, string token, Guid recipeId)
    {
        var (response, share) = await PostAuthorizedAsync<RecipeShareDto>(
            client, token, $"/api/recipes/{recipeId}/share", body: null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return share!;
    }

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

    private static async Task<(HttpResponseMessage Response, T? Data)> PutPhotoAuthorizedAsync<T>(
        HttpClient client, string token, Guid recipeId, byte[] bytes, string contentType, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/recipes/{recipeId}/photo");
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

    private sealed record Imported(Guid WrapperId, Guid SourceId);
}
