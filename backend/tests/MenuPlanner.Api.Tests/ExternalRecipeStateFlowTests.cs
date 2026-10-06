using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Families;
using MenuPlanner.Api.Plans;
using MenuPlanner.Api.Recipes;
using MenuPlanner.Api.ShoppingList;

namespace MenuPlanner.Api.Tests;

public sealed class ExternalRecipeStateFlowTests
{
    private const string Monday = "2026-09-07";

    [Fact]
    public async Task RevokedShare_MarksWrapperWarning_AndKeepsLiveContent()
    {
        using var client = new ApiFactory().CreateClient();
        var (owner, recipient, imported, _) = await ImportAsync(client);

        await DeleteAuthorizedAsync(client, owner.Token, $"/api/recipes/{imported.SourceId}/share");

        var (detailResponse, detail) = await GetAuthorizedAsync<RecipeDto>(
            client, recipient.Token, $"/api/recipes/{imported.WrapperId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.Equal("warning", detail!.State);
        Assert.Equal("Борщ", detail.Name);
        Assert.Equal("Классический борщ", detail.Description);
        Assert.NotEmpty(detail.Steps);

        var (_, list) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(
            client, recipient.Token, "/api/recipes?scope=external");
        Assert.Equal("warning", Assert.Single(list!).State);
    }

    [Fact]
    public async Task RegeneratedShare_MarksExistingWrapperWarning()
    {
        using var client = new ApiFactory().CreateClient();
        var (owner, recipient, imported, _) = await ImportAsync(client);

        await PostAuthorizedAsync<RecipeShareDto>(
            client, owner.Token, $"/api/recipes/{imported.SourceId}/share/regenerate", body: null);

        var (_, detail) = await GetAuthorizedAsync<RecipeDto>(
            client, recipient.Token, $"/api/recipes/{imported.WrapperId}");
        Assert.Equal("warning", detail!.State);

        var (_, list) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(
            client, recipient.Token, "/api/recipes?scope=external");
        Assert.Equal("warning", Assert.Single(list!).State);
    }

    [Fact]
    public async Task DeletedSource_MarksWrapperBroken_PlanAndShoppingList()
    {
        using var client = new ApiFactory().CreateClient();
        var (owner, recipient, imported, _) = await ImportAsync(client);

        var put = await PutAuthorizedAsync<WeekPlanDto>(client, recipient.Token,
            $"/api/plans/week/{Monday}", new SaveWeekPlanRequest(new[]
            {
                new PlanEntryRequest(0, "breakfast", imported.WrapperId, 1)
            }));
        Assert.Equal(HttpStatusCode.OK, put.Response.StatusCode);

        await DeleteAuthorizedAsync(client, owner.Token, $"/api/recipes/{imported.SourceId}");

        var (detailResponse, detail) = await GetAuthorizedAsync<RecipeDto>(
            client, recipient.Token, $"/api/recipes/{imported.WrapperId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.Equal("broken", detail!.State);
        Assert.Equal("Борщ", detail.Name);
        Assert.Empty(detail.Steps);
        Assert.Empty(detail.Ingredients);

        var (_, plan) = await GetAuthorizedAsync<WeekPlanDto>(
            client, recipient.Token, $"/api/plans/week/{Monday}");
        var entry = Assert.Single(plan!.Entries);
        Assert.Equal("broken", entry.State);
        Assert.Equal("Борщ", entry.RecipeName);

        var (_, shopping) = await GetAuthorizedAsync<ShoppingListDto>(
            client, recipient.Token, $"/api/shopping-list?weekStart={Monday}");
        Assert.Empty(shopping!.Items);
    }

    [Fact]
    public async Task RevokedShare_PlanEntryIsMarkedWarning()
    {
        using var client = new ApiFactory().CreateClient();
        var (owner, recipient, imported, _) = await ImportAsync(client);

        await PutAuthorizedAsync<WeekPlanDto>(client, recipient.Token,
            $"/api/plans/week/{Monday}", new SaveWeekPlanRequest(new[]
            {
                new PlanEntryRequest(0, "dinner", imported.WrapperId, 2)
            }));

        await DeleteAuthorizedAsync(client, owner.Token, $"/api/recipes/{imported.SourceId}/share");

        var (_, plan) = await GetAuthorizedAsync<WeekPlanDto>(
            client, recipient.Token, $"/api/plans/week/{Monday}");
        Assert.Equal("warning", Assert.Single(plan!.Entries).State);
    }

    [Fact]
    public async Task RemoveExternalLocally_DeletesWrapper_KeepsSourceIntact()
    {
        using var client = new ApiFactory().CreateClient();
        var (owner, recipient, imported, source) = await ImportAsync(client);

        var response = await DeleteAuthorizedAsync(
            client, recipient.Token, $"/api/recipes/{imported.WrapperId}/external");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var (_, list) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(
            client, recipient.Token, "/api/recipes");
        Assert.Empty(list!);

        var (sourceResponse, sourceDetail) = await GetAuthorizedAsync<RecipeDto>(
            client, owner.Token, $"/api/recipes/{imported.SourceId}");
        Assert.Equal(HttpStatusCode.OK, sourceResponse.StatusCode);
        Assert.Equal("Борщ", sourceDetail!.Name);
        Assert.Equal(source.Id, imported.SourceId);
    }

    [Fact]
    public async Task RemoveExternalLocally_OnOwnRecipe_IsRejected()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var own = await CreateRecipeAsync(client, owner.Token, "Свой суп");

        var response = await DeleteAuthorizedAsync(
            client, owner.Token, $"/api/recipes/{own.Id}/external");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<(AuthResponse Owner, AuthResponse Recipient, Imported Imported, RecipeDto Source)> ImportAsync(
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

        return (owner, recipient, new Imported(imported!.RecipeId, source.Id), source);
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
