using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Families;
using MenuPlanner.Api.Ingredients;
using MenuPlanner.Api.Plans;
using MenuPlanner.Api.Recipes;
using MenuPlanner.Api.Recipes.Repetition;
using MenuPlanner.Api.ShoppingList;

namespace MenuPlanner.Api.Tests;

public sealed class ExternalRecipeIntegrationFlowTests
{
    private const string Monday = "2026-09-07";

    [Fact]
    public async Task Plan_ShowsLiveName_AfterSourceRenamed()
    {
        using var client = new ApiFactory().CreateClient();
        var (owner, recipient, wrapperId, sourceId) = await ImportAsync(client, FullRequest("Борщ"));

        var put = await PutAuthorizedAsync<WeekPlanDto>(client, recipient.Token,
            $"/api/plans/week/{Monday}", new SaveWeekPlanRequest(new[]
            {
                new PlanEntryRequest(0, "dinner", wrapperId, 2)
            }));
        Assert.Equal(HttpStatusCode.OK, put.Response.StatusCode);
        Assert.Equal("Борщ", Assert.Single(put.Data!.Entries).RecipeName);

        await PutRecipeAsync(client, owner.Token, sourceId, FullRequest("Красный борщ"));

        var (_, plan) = await GetAuthorizedAsync<WeekPlanDto>(
            client, recipient.Token, $"/api/plans/week/{Monday}");
        var entry = Assert.Single(plan!.Entries);
        Assert.Equal("Красный борщ", entry.RecipeName);
        Assert.Equal("ok", entry.State);

        var (_, list) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(
            client, recipient.Token, "/api/recipes?scope=external");
        Assert.Equal("Красный борщ", Assert.Single(list!).Name);
    }

    [Fact]
    public async Task ShoppingList_ScalesLiveIngredients_ByCurrentSourceServings()
    {
        using var client = new ApiFactory().CreateClient();
        var (owner, recipient, wrapperId, sourceId) = await ImportAsync(client,
            FullRequest("Салат", servings: 6, ingredients: new[]
            {
                ("Свёкла", 2m, "pcs"),
                ("Соль", 1m, "tsp")
            }));

        await PutAuthorizedAsync<WeekPlanDto>(client, recipient.Token,
            $"/api/plans/week/{Monday}", new SaveWeekPlanRequest(new[]
            {
                new PlanEntryRequest(0, "lunch", wrapperId, 3)
            }));

        var (_, first) = await GetAuthorizedAsync<ShoppingListDto>(
            client, recipient.Token, $"/api/shopping-list?weekStart={Monday}");
        Assert.Equal(1m, Assert.Single(first!.Items, i => i.Name == "Свёкла").Amount);

        // Источник изменил и порции, и количества: закупка читает живые значения.
        await PutRecipeAsync(client, owner.Token, sourceId,
            FullRequest("Салат", servings: 3, ingredients: new[]
            {
                ("Свёкла", 4m, "pcs"),
                ("Соль", 2m, "tsp")
            }));

        var (_, second) = await GetAuthorizedAsync<ShoppingListDto>(
            client, recipient.Token, $"/api/shopping-list?weekStart={Monday}");
        Assert.Equal(4m, Assert.Single(second!.Items, i => i.Name == "Свёкла").Amount);
        Assert.Equal(2m, Assert.Single(second.Items, i => i.Name == "Соль").Amount);
    }

    [Fact]
    public async Task Autocomplete_IncludesExternalIngredients()
    {
        using var client = new ApiFactory().CreateClient();
        var (_, recipient, _, _) = await ImportAsync(client,
            FullRequest("Салат", ingredients: new[]
            {
                ("Свёкла", 2m, "pcs"),
                ("Сметана", 100m, "g")
            }));

        var (_, all) = await GetAuthorizedAsync<IngredientAutocompleteDto>(
            client, recipient.Token, "/api/ingredients/autocomplete");
        Assert.Contains("Свёкла", all!.Items);
        Assert.Contains("Сметана", all.Items);

        var (_, filtered) = await GetAuthorizedAsync<IngredientAutocompleteDto>(
            client, recipient.Token, "/api/ingredients/autocomplete?q=свё");
        Assert.Contains("Свёкла", filtered!.Items);
    }

    [Fact]
    public async Task Match_UsesLiveExternalContent()
    {
        using var client = new ApiFactory().CreateClient();
        var (_, recipient, wrapperId, _) = await ImportAsync(client,
            FullRequest("Борщ", difficulty: 3, calories: 350, ingredients: new[]
            {
                ("Свёкла", 2m, "pcs")
            }));

        var (_, all) = await PostMatchAsync<RecipeMatchResponse>(
            client, recipient.Token, new RecipeMatchRequest());
        var item = Assert.Single(all!.Items);
        Assert.Equal(wrapperId, item.RecipeId);
        Assert.Equal("Борщ", item.Name);
        Assert.Equal(3, item.Difficulty);
        Assert.Equal(350, item.Calories);

        var (_, byIngredient) = await PostMatchAsync<RecipeMatchResponse>(client, recipient.Token,
            new RecipeMatchRequest(new MatchFilters(IncludeIngredients: new List<string> { "свёкла" })));
        Assert.Equal(wrapperId, Assert.Single(byIngredient!.Items).RecipeId);

        var (_, hard) = await PostMatchAsync<RecipeMatchResponse>(client, recipient.Token,
            new RecipeMatchRequest(new MatchFilters(MaxDifficulty: 2)));
        Assert.Empty(hard!.Items);
    }

    [Fact]
    public async Task Repetition_CountsExternalPlanEntries()
    {
        using var client = new ApiFactory().CreateClient();
        var (_, recipient, wrapperId, _) = await ImportAsync(client, FullRequest("Борщ"));
        var monday = CurrentMonday().ToString("yyyy-MM-dd");

        await PutAuthorizedAsync<WeekPlanDto>(client, recipient.Token,
            $"/api/plans/week/{monday}", new SaveWeekPlanRequest(new[]
            {
                new PlanEntryRequest(0, "dinner", wrapperId, 2)
            }));

        var (_, list) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(
            client, recipient.Token, "/api/recipes");
        Assert.Equal(1, Assert.Single(list!).RepetitionCount);

        var (_, repetition) = await GetAuthorizedAsync<List<RecipeRepetitionDto>>(
            client, recipient.Token, "/api/recipes/repetition");
        var entry = Assert.Single(repetition!);
        Assert.Equal(wrapperId, entry.RecipeId);
        Assert.Equal(1, entry.Count);
    }

    [Fact]
    public async Task RevokedShare_StillContributesLiveIngredientsToShoppingList()
    {
        using var client = new ApiFactory().CreateClient();
        var (owner, recipient, wrapperId, sourceId) = await ImportAsync(client,
            FullRequest("Салат", servings: 2, ingredients: new[]
            {
                ("Мука", 100m, "g")
            }));

        await PutAuthorizedAsync<WeekPlanDto>(client, recipient.Token,
            $"/api/plans/week/{Monday}", new SaveWeekPlanRequest(new[]
            {
                new PlanEntryRequest(0, "lunch", wrapperId, 2)
            }));

        await DeleteAuthorizedAsync(client, owner.Token, $"/api/recipes/{sourceId}/share");

        var (_, shopping) = await GetAuthorizedAsync<ShoppingListDto>(
            client, recipient.Token, $"/api/shopping-list?weekStart={Monday}");
        var flour = Assert.Single(shopping!.Items);
        Assert.Equal("Мука", flour.Name);
        Assert.Equal(100m, flour.Amount);

        var (_, plan) = await GetAuthorizedAsync<WeekPlanDto>(
            client, recipient.Token, $"/api/plans/week/{Monday}");
        Assert.Equal("warning", Assert.Single(plan!.Entries).State);
    }

    private static async Task<(AuthResponse Owner, AuthResponse Recipient, Guid WrapperId, Guid SourceId)> ImportAsync(
        HttpClient client, RecipeRequest source)
    {
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья источника");
        var created = await CreateRecipeAsync(client, owner.Token, source);
        var share = await ShareAsync(client, owner.Token, created.Id);

        var recipient = await RegisterAsync(client, "recipient");
        await CreateFamilyAsync(client, recipient.Token, "Семья получателя");
        var (_, imported) = await PostAuthorizedAsync<RecipeImportResultDto>(
            client, recipient.Token, $"/api/shared/{share.Token}/import", body: null);

        return (owner, recipient, imported!.RecipeId, created.Id);
    }

    private static RecipeRequest FullRequest(
        string name,
        int servings = 6,
        int difficulty = 3,
        int? calories = 350,
        IReadOnlyList<(string Name, decimal Amount, string Unit)>? ingredients = null,
        IReadOnlyList<string>? seasonality = null) => new(
        Name: name,
        Description: "Классический рецепт",
        CookTimeMinutes: 90,
        Servings: servings,
        Difficulty: difficulty,
        Calories: calories,
        Tags: new List<string> { "суп" },
        Seasonality: (seasonality ?? new List<string> { "winter" }).ToList(),
        Diet: new List<string>(),
        Steps: new List<RecipeStepRequest> { new("Сварить бульон.") },
        Ingredients: (ingredients ?? new[] { ("Свёкла", 2m, "pcs") })
            .Select(i => new RecipeIngredientRequest(i.Name, i.Amount, i.Unit, null))
            .ToList());

    private static async Task<RecipeShareDto> ShareAsync(HttpClient client, string token, Guid recipeId)
    {
        var (response, share) = await GetAuthorizedAsync<RecipeShareDto>(
            client, token, $"/api/recipes/{recipeId}/share");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return share!;
    }

    private static async Task<RecipeDto> CreateRecipeAsync(HttpClient client, string token, RecipeRequest request)
    {
        var (response, recipe) = await PostAuthorizedAsync<RecipeDto>(client, token, "/api/recipes", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(recipe);
        return recipe!;
    }

    private static async Task<RecipeDto> PutRecipeAsync(
        HttpClient client, string token, Guid recipeId, RecipeRequest request)
    {
        var (response, recipe) = await PutAuthorizedAsync<RecipeDto>(
            client, token, $"/api/recipes/{recipeId}", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(recipe);
        return recipe!;
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

    private static DateOnly CurrentMonday()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var offset = ((int)today.DayOfWeek + 6) % 7;
        return today.AddDays(-offset);
    }

    private static async Task<(HttpResponseMessage Response, T? Data)> PostMatchAsync<T>(
        HttpClient client, string token, object? body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/recipes/match");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(body ?? new RecipeMatchRequest());
        var response = await client.SendAsync(request);
        var data = await ReadJsonAsync<T>(response);
        return (response, data);
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
}
