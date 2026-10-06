using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Families;
using MenuPlanner.Api.Recipes;

namespace MenuPlanner.Api.Tests;

public sealed class ExternalRecipeImportTests
{
    [Fact]
    public async Task Import_AddsExternalWrapper_AndReadsLiveContent()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья источника");
        var source = await CreateRecipeAsync(client, owner.Token, "Борщ", FullRequest());
        var share = await ShareAsync(client, owner.Token, source.Id);

        var recipient = await RegisterAsync(client, "recipient");
        await CreateFamilyAsync(client, recipient.Token, "Семья получателя");

        var (importResponse, imported) = await PostAuthorizedAsync<RecipeImportResultDto>(
            client, recipient.Token, $"/api/shared/{share.Token}/import", body: null);
        Assert.Equal(HttpStatusCode.Created, importResponse.StatusCode);
        Assert.NotNull(imported);

        var (listResponse, list) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(
            client, recipient.Token, "/api/recipes");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var summary = Assert.Single(list!);
        Assert.Equal(imported.RecipeId, summary.Id);
        Assert.True(summary.IsExternal);
        Assert.Equal("Семья источника", summary.SourceFamilyName);
        Assert.Equal("ok", summary.State);
        Assert.Equal("Борщ", summary.Name);

        var (detailResponse, detail) = await GetAuthorizedAsync<RecipeDto>(
            client, recipient.Token, $"/api/recipes/{imported.RecipeId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.Equal(imported.RecipeId, detail!.Id);
        Assert.True(detail.IsExternal);
        Assert.Equal("ok", detail.State);
        Assert.Equal("Семья источника", detail.SourceFamilyName);
        Assert.Equal("Классический борщ", detail.Description);
        Assert.Equal(90, detail.CookTimeMinutes);
        Assert.Equal(6, detail.Servings);
        Assert.Equal(3, detail.Difficulty);
        Assert.Equal(350, detail.Calories);
        Assert.Equal(new[] { "Сварить бульон.", "Добавить свёклу." }, detail.Steps);
        Assert.Equal(2, detail.Ingredients.Count);
        Assert.Equal("Свёкла", detail.Ingredients[0].Name);
    }

    [Fact]
    public async Task Import_SourceEdit_IsVisibleLive_AndRefreshesNameCache()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья источника");
        var source = await CreateRecipeAsync(client, owner.Token, "Борщ", FullRequest());
        var share = await ShareAsync(client, owner.Token, source.Id);

        var recipient = await RegisterAsync(client, "recipient");
        await CreateFamilyAsync(client, recipient.Token, "Семья получателя");
        var (_, imported) = await PostAuthorizedAsync<RecipeImportResultDto>(
            client, recipient.Token, $"/api/shared/{share.Token}/import", body: null);

        var (updateResponse, _) = await PutAuthorizedAsync<RecipeDto>(
            client, owner.Token, $"/api/recipes/{source.Id}",
            FullRequest() with { Name = "Борщ по-домашнему", CookTimeMinutes = 120, Servings = 8, Revision = source.Revision });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var (_, detail) = await GetAuthorizedAsync<RecipeDto>(
            client, recipient.Token, $"/api/recipes/{imported!.RecipeId}");
        Assert.Equal("Борщ по-домашнему", detail!.Name);
        Assert.Equal(120, detail.CookTimeMinutes);
        Assert.Equal(8, detail.Servings);

        var (_, list) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(
            client, recipient.Token, "/api/recipes");
        Assert.Equal("Борщ по-домашнему", Assert.Single(list!).Name);
    }

    [Fact]
    public async Task List_ExternalRecipe_ShowsLiveSourcePhoto()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья источника");
        var source = await CreateRecipeAsync(client, owner.Token, "Борщ", FullRequest());
        var (photoResponse, uploaded) = await PutPhotoAuthorizedAsync<RecipeDto>(
            client, owner.Token, source.Id, source.Revision, new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png", "photo.png");
        Assert.Equal(HttpStatusCode.OK, photoResponse.StatusCode);
        Assert.NotNull(uploaded!.PhotoUrl);
        var share = await ShareAsync(client, owner.Token, source.Id);

        var recipient = await RegisterAsync(client, "recipient");
        await CreateFamilyAsync(client, recipient.Token, "Семья получателя");
        await PostAuthorizedAsync<RecipeImportResultDto>(
            client, recipient.Token, $"/api/shared/{share.Token}/import", body: null);

        var (_, list) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(
            client, recipient.Token, "/api/recipes?scope=external");
        var item = Assert.Single(list!);
        Assert.Equal(uploaded.PhotoUrl, item.PhotoUrl);
    }

    [Fact]
    public async Task List_ExternalRecipe_RefreshesCachedNameOnRead()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья источника");
        var source = await CreateRecipeAsync(client, owner.Token, "Борщ", FullRequest());
        var share = await ShareAsync(client, owner.Token, source.Id);

        var recipient = await RegisterAsync(client, "recipient");
        await CreateFamilyAsync(client, recipient.Token, "Семья получателя");
        await PostAuthorizedAsync<RecipeImportResultDto>(
            client, recipient.Token, $"/api/shared/{share.Token}/import", body: null);

        // Чтение списка после переименования источника обновляет кэш имени.
        var (_, renamed) = await PutAuthorizedAsync<RecipeDto>(
            client, owner.Token, $"/api/recipes/{source.Id}",
            FullRequest() with { Name = "Борщ по-домашнему", Revision = source.Revision });
        await GetAuthorizedAsync<List<RecipeSummaryDto>>(client, recipient.Token, "/api/recipes");

        // Источник удалён: живого контента нет, остаётся кэш — он должен быть актуальным.
        await DeleteAuthorizedAsync(client, owner.Token, $"/api/recipes/{source.Id}?revision={renamed!.Revision}");

        var (_, list) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(
            client, recipient.Token, "/api/recipes?scope=external");
        var item = Assert.Single(list!);
        Assert.Equal("broken", item.State);
        Assert.Equal("Борщ по-домашнему", item.Name);
    }

    [Fact]
    public async Task Import_WithoutFamily_ReturnsNotFound()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var source = await CreateRecipeAsync(client, owner.Token, "Борщ", FullRequest());
        var share = await ShareAsync(client, owner.Token, source.Id);

        var lonely = await RegisterAsync(client, "lonely");

        var (response, error) = await PostAuthorizedAsync<RecipeErrorDto>(
            client, lonely.Token, $"/api/shared/{share.Token}/import", body: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Вы не состоите в семье.", error!.Error);
    }

    [Fact]
    public async Task Import_WithoutToken_ReturnsUnauthorized()
    {
        using var client = new ApiFactory().CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/shared/{Guid.NewGuid():N}/import", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Import_FromSourceFamily_IsBlocked()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var source = await CreateRecipeAsync(client, owner.Token, "Борщ", FullRequest());
        var share = await ShareAsync(client, owner.Token, source.Id);

        var (response, error) = await PostAuthorizedAsync<RecipeErrorDto>(
            client, owner.Token, $"/api/shared/{share.Token}/import", body: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Это рецепт вашей семьи.", error!.Error);
    }

    [Fact]
    public async Task Import_Twice_ReturnsConflict_WithExistingWrapperId()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья источника");
        var source = await CreateRecipeAsync(client, owner.Token, "Борщ", FullRequest());
        var share = await ShareAsync(client, owner.Token, source.Id);

        var recipient = await RegisterAsync(client, "recipient");
        await CreateFamilyAsync(client, recipient.Token, "Семья получателя");

        var (_, first) = await PostAuthorizedAsync<RecipeImportResultDto>(
            client, recipient.Token, $"/api/shared/{share.Token}/import", body: null);

        var (response, conflict) = await PostAuthorizedAsync<RecipeImportConflictDto>(
            client, recipient.Token, $"/api/shared/{share.Token}/import", body: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Рецепт уже добавлен в вашу семью.", conflict!.Error);
        Assert.Equal(first!.RecipeId, conflict.RecipeId);

        var (_, list) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(
            client, recipient.Token, "/api/recipes");
        Assert.Single(list!);
    }

    [Fact]
    public async Task Import_RaceOnUniqueIndex_ReturnsConflictWithExistingExternalRecipe()
    {
        using var factory = new ApiFactory();
        var race = new RaceInjectingInterceptor(factory.DatabaseName, factory.DatabaseRoot);
        factory.Interceptors.Add(race);
        using var client = factory.CreateClient();

        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья источника");
        var source = await CreateRecipeAsync(client, owner.Token, "Борщ", FullRequest());
        var share = await ShareAsync(client, owner.Token, source.Id);

        var recipient = await RegisterAsync(client, "recipient");
        var recipientFamily = await CreateFamilyAsync(client, recipient.Token, "Семья получателя");

        // Гонка: «параллельный» импорт успевает вставить внешний рецепт до нашего SaveChanges.
        var concurrentId = Guid.NewGuid();
        race.Arm(recipientFamily.Id, source.Id, concurrentId);

        var (response, conflict) = await PostAuthorizedAsync<RecipeImportConflictDto>(
            client, recipient.Token, $"/api/shared/{share.Token}/import", body: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Рецепт уже добавлен в вашу семью.", conflict!.Error);
        Assert.Equal(concurrentId, conflict.RecipeId);

        var (_, list) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(
            client, recipient.Token, "/api/recipes?scope=external");
        Assert.Equal(concurrentId, Assert.Single(list!).Id);
    }

    [Fact]
    public async Task Import_UnverifiedUser_ReturnsForbidden()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья источника");
        var source = await CreateRecipeAsync(client, owner.Token, "Борщ", FullRequest());
        var share = await ShareAsync(client, owner.Token, source.Id);

        var recipient = await RegisterAsync(client, "recipient");
        await CreateFamilyAsync(client, recipient.Token, "Семья получателя");
        await UnverifyAsync(factory, recipient.User.Email);

        var (response, _) = await PostAuthorizedAsync<RecipeErrorDto>(
            client, recipient.Token, $"/api/shared/{share.Token}/import", body: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public void ExternalRecipe_HasUniqueFilteredIndex()
    {
        using var factory = new ApiFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var index = db.Model.FindEntityType(typeof(Recipe))!
            .GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name)
                .SequenceEqual(new[] { nameof(Recipe.FamilyId), nameof(Recipe.SourceRecipeId) }));

        Assert.True(index.IsUnique);
        Assert.NotNull(index.GetFilter());
    }

    [Fact]
    public async Task Import_RevokedToken_ReturnsInvalidLink()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья источника");
        var source = await CreateRecipeAsync(client, owner.Token, "Борщ", FullRequest());
        var share = await ShareAsync(client, owner.Token, source.Id);
        await DeleteAuthorizedAsync(client, owner.Token, $"/api/recipes/{source.Id}/share");

        var recipient = await RegisterAsync(client, "recipient");
        await CreateFamilyAsync(client, recipient.Token, "Семья получателя");

        var (response, error) = await PostAuthorizedAsync<RecipeErrorDto>(
            client, recipient.Token, $"/api/shared/{share.Token}/import", body: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Ссылка недействительна.", error!.Error);
    }

    [Fact]
    public async Task Import_DeletedSource_ReturnsInvalidLink()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья источника");
        var source = await CreateRecipeAsync(client, owner.Token, "Борщ", FullRequest());
        var share = await ShareAsync(client, owner.Token, source.Id);
        await DeleteAuthorizedAsync(client, owner.Token,
            $"/api/recipes/{source.Id}?revision={source.Revision}");

        var recipient = await RegisterAsync(client, "recipient");
        await CreateFamilyAsync(client, recipient.Token, "Семья получателя");

        var (response, error) = await PostAuthorizedAsync<RecipeErrorDto>(
            client, recipient.Token, $"/api/shared/{share.Token}/import", body: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Ссылка недействительна.", error!.Error);
    }

    [Fact]
    public async Task Import_UnknownToken_ReturnsInvalidLink()
    {
        using var client = new ApiFactory().CreateClient();
        var recipient = await RegisterAsync(client, "recipient");
        await CreateFamilyAsync(client, recipient.Token, "Семья получателя");

        var (response, error) = await PostAuthorizedAsync<RecipeErrorDto>(
            client, recipient.Token, $"/api/shared/{Guid.NewGuid():N}/import", body: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Ссылка недействительна.", error!.Error);
    }

    [Fact]
    public async Task List_ScopeFilter_SplitsOwnAndExternal()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья источника");
        var source = await CreateRecipeAsync(client, owner.Token, "Чужой борщ", FullRequest() with { Name = "Чужой борщ" });
        var share = await ShareAsync(client, owner.Token, source.Id);

        var recipient = await RegisterAsync(client, "recipient");
        await CreateFamilyAsync(client, recipient.Token, "Семья получателя");
        await CreateRecipeAsync(client, recipient.Token, "Свой суп", FullRequest() with { Name = "Свой суп" });
        await CreateRecipeAsync(client, recipient.Token, "Свои блины", FullRequest() with { Name = "Свои блины" });
        await PostAuthorizedAsync<RecipeImportResultDto>(
            client, recipient.Token, $"/api/shared/{share.Token}/import", body: null);

        var (_, all) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(client, recipient.Token, "/api/recipes");
        Assert.Equal(3, all!.Count);

        var (_, own) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(client, recipient.Token, "/api/recipes?scope=own");
        Assert.Equal(2, own!.Count);
        Assert.All(own, r => Assert.False(r.IsExternal));

        var (_, external) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(client, recipient.Token, "/api/recipes?scope=external");
        var item = Assert.Single(external!);
        Assert.True(item.IsExternal);
        Assert.Equal("Чужой борщ", item.Name);
    }

    [Fact]
    public async Task ExternalRecipe_EditDeleteAndPhoto_ReturnForbidden()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var externalId = await CreateImportedRecipeAsync(client);

        var (updateResponse, _) = await PutAuthorizedAsync<RecipeErrorDto>(
            client, externalId.RecipientToken, $"/api/recipes/{externalId.RecipeId}", FullRequest());
        Assert.Equal(HttpStatusCode.Forbidden, updateResponse.StatusCode);

        var deleteResponse = await DeleteAuthorizedAsync(
            client, externalId.RecipientToken, $"/api/recipes/{externalId.RecipeId}");
        Assert.Equal(HttpStatusCode.Forbidden, deleteResponse.StatusCode);

        var (photoResponse, _) = await PutPhotoAuthorizedAsync<RecipeErrorDto>(
            client, externalId.RecipientToken, externalId.RecipeId, null,
            new byte[] { 1, 2, 3 }, "image/png", "photo.png");
        Assert.Equal(HttpStatusCode.Forbidden, photoResponse.StatusCode);

        var deletePhotoResponse = await DeleteAuthorizedAsync(
            client, externalId.RecipientToken, $"/api/recipes/{externalId.RecipeId}/photo");
        Assert.Equal(HttpStatusCode.Forbidden, deletePhotoResponse.StatusCode);
    }

    [Fact]
    public async Task ExternalRecipe_CannotBeSharedFurther()
    {
        using var client = new ApiFactory().CreateClient();
        var externalId = await CreateImportedRecipeAsync(client);

        var (response, _) = await GetAuthorizedAsync<RecipeErrorDto>(
            client, externalId.RecipientToken, $"/api/recipes/{externalId.RecipeId}/share");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeletedSource_MarksExternalRecipeBroken_AndKeepsCachedName()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья источника");
        var source = await CreateRecipeAsync(client, owner.Token, "Борщ", FullRequest());
        var share = await ShareAsync(client, owner.Token, source.Id);

        var recipient = await RegisterAsync(client, "recipient");
        await CreateFamilyAsync(client, recipient.Token, "Семья получателя");
        var (_, imported) = await PostAuthorizedAsync<RecipeImportResultDto>(
            client, recipient.Token, $"/api/shared/{share.Token}/import", body: null);

        await DeleteAuthorizedAsync(client, owner.Token,
            $"/api/recipes/{source.Id}?revision={source.Revision}");

        var (response, detail) = await GetAuthorizedAsync<RecipeDto>(
            client, recipient.Token, $"/api/recipes/{imported!.RecipeId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("broken", detail!.State);
        Assert.Equal("Борщ", detail.Name);
        Assert.Empty(detail.Steps);
        Assert.Empty(detail.Ingredients);

        var (_, list) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(
            client, recipient.Token, "/api/recipes?scope=external");
        Assert.Equal("broken", Assert.Single(list!).State);
    }

    private static async Task<ImportedRecipe> CreateImportedRecipeAsync(HttpClient client)
    {
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья источника");
        var source = await CreateRecipeAsync(client, owner.Token, "Борщ", FullRequest());
        var share = await ShareAsync(client, owner.Token, source.Id);

        var recipient = await RegisterAsync(client, "recipient");
        await CreateFamilyAsync(client, recipient.Token, "Семья получателя");
        var (_, imported) = await PostAuthorizedAsync<RecipeImportResultDto>(
            client, recipient.Token, $"/api/shared/{share.Token}/import", body: null);

        return new ImportedRecipe(imported!.RecipeId, recipient.Token);
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

    private static async Task<RecipeShareDto> ShareAsync(HttpClient client, string token, Guid recipeId)
    {
        var (response, share) = await PostAuthorizedAsync<RecipeShareDto>(
            client, token, $"/api/recipes/{recipeId}/share", body: null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(share);
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
        return auth;
    }

    private static async Task<FamilyDto> CreateFamilyAsync(HttpClient client, string token, string name)
    {
        var (response, family) = await PostAuthorizedAsync<FamilyDto>(client, token, "/api/families", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(family);
        return family!;
    }

    private static async Task UnverifyAsync(ApiFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        user.IsEmailVerified = false;
        user.EmailVerifiedAt = null;
        await db.SaveChangesAsync();
    }

    private static async Task<RecipeDto> CreateRecipeAsync(
        HttpClient client, string token, string name, RecipeRequest request)
    {
        var (response, recipe) = await PostAuthorizedAsync<RecipeDto>(
            client, token, "/api/recipes", request with { Name = name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(recipe);
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
        HttpClient client, string token, Guid recipeId, int? revision, byte[] bytes, string contentType, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);

        var path = $"/api/recipes/{recipeId}/photo" + (revision is null ? "" : $"?revision={revision}");
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
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

    private sealed record ImportedRecipe(Guid RecipeId, string RecipientToken);

    /// <summary>
    /// Симулирует гонку импорта: при вставке внешнего рецепта сначала создаёт
    /// «параллельный» дубль в той же in-memory базе, а затем бросает <see cref="DbUpdateException"/>,
    /// как это сделал бы уникальный индекс <c>(FamilyId, SourceRecipeId)</c>.
    /// </summary>
    private sealed class RaceInjectingInterceptor : SaveChangesInterceptor
    {
        private readonly string _databaseName;
        private readonly InMemoryDatabaseRoot _databaseRoot;
        private bool _armed;
        private bool _fired;
        private Guid _familyId;
        private Guid _sourceId;
        private Guid _duplicateId;

        public RaceInjectingInterceptor(string databaseName, InMemoryDatabaseRoot databaseRoot)
        {
            _databaseName = databaseName;
            _databaseRoot = databaseRoot;
        }

        public void Arm(Guid familyId, Guid sourceId, Guid duplicateId)
        {
            _familyId = familyId;
            _sourceId = sourceId;
            _duplicateId = duplicateId;
            _armed = true;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (_armed && !_fired && eventData.Context is AppDbContext context)
            {
                var isExternalInsert = context.ChangeTracker.Entries<Recipe>()
                    .Any(e => e.State == EntityState.Added && e.Entity.SourceRecipeId == _sourceId);
                if (isExternalInsert)
                {
                    _fired = true;
                    await InsertDuplicateAsync(cancellationToken);
                    throw new DbUpdateException(
                        "Симуляция нарушения уникального индекса (FamilyId, SourceRecipeId).");
                }
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private async Task InsertDuplicateAsync(CancellationToken cancellationToken)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(_databaseName, _databaseRoot)
                .Options;
            using var db = new AppDbContext(options);
            db.Recipes.Add(new Recipe
            {
                Id = _duplicateId,
                FamilyId = _familyId,
                Name = "Борщ",
                SourceRecipeId = _sourceId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
