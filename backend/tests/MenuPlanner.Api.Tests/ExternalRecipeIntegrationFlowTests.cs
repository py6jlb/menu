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
    public async Task List_ShowsLiveMetadata_AfterSourceEdited()
    {
        using var client = new ApiFactory().CreateClient();
        var (owner, recipient, wrapperId, sourceId) = await ImportAsync(client,
            FullRequest("Борщ", servings: 6, difficulty: 3));

        var (_, before) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(
            client, recipient.Token, "/api/recipes?scope=external");
        var initial = Assert.Single(before!);
        Assert.Equal(3, initial.Difficulty);
        Assert.Equal(6, initial.Servings);
        Assert.Equal(new[] { "суп" }, initial.Tags);

        await PutRecipeAsync(client, owner.Token, sourceId,
            FullRequest("Борщ", servings: 2, difficulty: 1) with
            {
                Tags = new List<string> { "острое", "зимнее" },
                Seasonality = new List<string> { "autumn" },
                Diet = new List<string> { "вегетарианское" }
            });

        var (_, after) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(
            client, recipient.Token, "/api/recipes?scope=external");
        var updated = Assert.Single(after!);
        Assert.Equal(wrapperId, updated.Id);
        Assert.Equal(1, updated.Difficulty);
        Assert.Equal(2, updated.Servings);
        Assert.Equal(new[] { "острое", "зимнее" }, updated.Tags);
        Assert.Equal(new[] { "autumn" }, updated.Seasonality);
        Assert.Equal(new[] { "vegetarian" }, updated.Diet);
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
        Assert.Contains(all!.Items, i => i.Name == "Свёкла");
        Assert.Contains(all.Items, i => i.Name == "Сметана");

        var (_, filtered) = await GetAuthorizedAsync<IngredientAutocompleteDto>(
            client, recipient.Token, "/api/ingredients/autocomplete?q=свё");
        Assert.Contains(filtered!.Items, i => i.Name == "Свёкла");
    }

    [Fact]
    public async Task Autocomplete_ReflectsLiveSourceEdits()
    {
        using var client = new ApiFactory().CreateClient();
        var (owner, recipient, _, sourceId) = await ImportAsync(client,
            FullRequest("Салат", ingredients: new[]
            {
                ("Свёкла", 2m, "pcs")
            }));

        var (_, before) = await GetAuthorizedAsync<IngredientAutocompleteDto>(
            client, recipient.Token, "/api/ingredients/autocomplete");
        Assert.Contains(before!.Items, i => i.Name == "Свёкла");

        await PutRecipeAsync(client, owner.Token, sourceId,
            FullRequest("Салат", ingredients: new[]
            {
                ("Капуста", 1m, "pcs")
            }));

        var (_, after) = await GetAuthorizedAsync<IngredientAutocompleteDto>(
            client, recipient.Token, "/api/ingredients/autocomplete");
        Assert.Contains(after!.Items, i => i.Name == "Капуста");
        Assert.DoesNotContain(after.Items, i => i.Name == "Свёкла");
    }

    [Fact]
    public async Task Autocomplete_WarningExternal_StillIncludesLiveIngredients()
    {
        using var client = new ApiFactory().CreateClient();
        var (owner, recipient, _, sourceId) = await ImportAsync(client,
            FullRequest("Салат", ingredients: new[]
            {
                ("Морковь", 2m, "pcs")
            }));

        await DeleteAuthorizedAsync(client, owner.Token, $"/api/recipes/{sourceId}/share");

        var (_, items) = await GetAuthorizedAsync<IngredientAutocompleteDto>(
            client, recipient.Token, "/api/ingredients/autocomplete");
        Assert.Contains(items!.Items, i => i.Name == "Морковь");
    }

    [Fact]
    public async Task Autocomplete_BrokenExternal_DoesNotIncludeStaleIngredients()
    {
        using var client = new ApiFactory().CreateClient();
        var (owner, recipient, _, sourceId) = await ImportAsync(client,
            FullRequest("Салат", ingredients: new[]
            {
                ("Сельдерей", 2m, "pcs")
            }));
        var (_, sourceDetail) = await GetAuthorizedAsync<RecipeDto>(
            client, owner.Token, $"/api/recipes/{sourceId}");

        var delete = await DeleteAuthorizedAsync(
            client, owner.Token, $"/api/recipes/{sourceId}?revision={sourceDetail!.Revision}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var (_, items) = await GetAuthorizedAsync<IngredientAutocompleteDto>(
            client, recipient.Token, "/api/ingredients/autocomplete");
        Assert.DoesNotContain(items!.Items, i => i.Name == "Сельдерей");
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
        // Внешний рецепт не маскируется под обычный: видно происхождение и состояние.
        Assert.True(item.IsExternal);
        Assert.Equal("ok", item.State);
        Assert.Equal("Семья источника", item.SourceFamilyName);

        var (_, byIngredient) = await PostMatchAsync<RecipeMatchResponse>(client, recipient.Token,
            new RecipeMatchRequest(new MatchFilters(IncludeIngredients: new List<string> { "свёкла" })));
        Assert.Equal(wrapperId, Assert.Single(byIngredient!.Items).RecipeId);

        var (_, hard) = await PostMatchAsync<RecipeMatchResponse>(client, recipient.Token,
            new RecipeMatchRequest(new MatchFilters(MaxDifficulty: 2)));
        Assert.Empty(hard!.Items);
    }

    [Fact]
    public async Task Match_WarningExternal_IsIncludedWithStateAndOrigin()
    {
        using var client = new ApiFactory().CreateClient();
        var (owner, recipient, wrapperId, sourceId) = await ImportAsync(client, FullRequest("Борщ"));

        await DeleteAuthorizedAsync(client, owner.Token, $"/api/recipes/{sourceId}/share");

        var (_, all) = await PostMatchAsync<RecipeMatchResponse>(client, recipient.Token, new RecipeMatchRequest());

        var item = Assert.Single(all!.Items);
        Assert.Equal(wrapperId, item.RecipeId);
        Assert.True(item.IsExternal);
        Assert.Equal("warning", item.State);
        Assert.Equal("Семья источника", item.SourceFamilyName);
    }

    [Fact]
    public async Task Match_BrokenExternal_IsExcludedFromAvailableDishes()
    {
        using var client = new ApiFactory().CreateClient();
        var (owner, recipient, _, sourceId) = await ImportAsync(client, FullRequest("Борщ"));
        var (_, sourceDetail) = await GetAuthorizedAsync<RecipeDto>(
            client, owner.Token, $"/api/recipes/{sourceId}");

        var delete = await DeleteAuthorizedAsync(
            client, owner.Token, $"/api/recipes/{sourceId}?revision={sourceDetail!.Revision}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var (_, all) = await PostMatchAsync<RecipeMatchResponse>(client, recipient.Token, new RecipeMatchRequest());

        Assert.Empty(all!.Items);
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
    public async Task Match_Repetition_CountsExternalByLocalIdentity()
    {
        using var client = new ApiFactory().CreateClient();
        var (_, recipient, wrapperId, _) = await ImportAsync(client, FullRequest("Борщ"));

        var selected = CurrentMonday();
        // Две записи в одной неделе и одна в предыдущей: разные недели → 2.
        await PutAuthorizedAsync<WeekPlanDto>(client, recipient.Token,
            $"/api/plans/week/{selected:yyyy-MM-dd}", new SaveWeekPlanRequest(new[]
            {
                new PlanEntryRequest(0, "dinner", wrapperId, 2),
                new PlanEntryRequest(4, "dinner", wrapperId, 2)
            }));
        await PutAuthorizedAsync<WeekPlanDto>(client, recipient.Token,
            $"/api/plans/week/{selected.AddDays(-7):yyyy-MM-dd}", new SaveWeekPlanRequest(new[]
            {
                new PlanEntryRequest(0, "dinner", wrapperId, 2)
            }));

        var (_, match) = await PostMatchAsync<RecipeMatchResponse>(
            client, recipient.Token, new RecipeMatchRequest(WeekStart: selected));

        var item = Assert.Single(match!.Items);
        // Внешний рецепт сохраняет локальную identity подсчёта (id обёртки), а не id источника.
        Assert.Equal(wrapperId, item.RecipeId);
        Assert.True(item.IsExternal);
        Assert.Equal(2, item.RepetitionCount);
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

    [Fact]
    public async Task ShoppingList_BrokenSource_IsExcludedWithPlanEntryAndReason()
    {
        using var client = new ApiFactory().CreateClient();
        var (owner, recipient, wrapperId, sourceId) = await ImportAsync(client,
            FullRequest("Салат", servings: 2, ingredients: new[] { ("Мука", 100m, "g") }));

        await PutAuthorizedAsync<WeekPlanDto>(client, recipient.Token,
            $"/api/plans/week/{Monday}", new SaveWeekPlanRequest(new[]
            {
                new PlanEntryRequest(2, "dinner", wrapperId, 2)
            }));

        var (_, sourceDetail) = await GetAuthorizedAsync<RecipeDto>(
            client, owner.Token, $"/api/recipes/{sourceId}");
        await DeleteAuthorizedAsync(
            client, owner.Token, $"/api/recipes/{sourceId}?revision={sourceDetail!.Revision}");

        var (_, list) = await GetAuthorizedAsync<ShoppingListDto>(
            client, recipient.Token, $"/api/shopping-list?weekStart={Monday}");

        Assert.True(list!.HasPlan);
        Assert.Empty(list.Items);
        var excluded = Assert.Single(list.Excluded);
        Assert.Equal(2, excluded.Day);
        Assert.Equal("dinner", excluded.MealType);
        Assert.Equal(wrapperId, excluded.RecipeId);
        Assert.Equal("Салат", excluded.RecipeName);
        Assert.Equal(ShoppingListContentBuilder.SourceMissingReason, excluded.Reason);
    }

    [Fact]
    public async Task ShoppingList_MixedOwnWarningBroken_SumsAvailableAndReportsBroken()
    {
        using var client = new ApiFactory().CreateClient();
        // Внешний с отозванной ссылкой: source жив → warning, живой контент участвует в расчёте.
        var (owner, recipient, warningWrapperId, warningSourceId) = await ImportAsync(client,
            FullRequest("Салат", servings: 2, ingredients: new[] { ("Мука", 100m, "g") }));
        await DeleteAuthorizedAsync(client, owner.Token, $"/api/recipes/{warningSourceId}/share");

        // Второй внешний, чей источник будет удалён → broken.
        var brokenSource = await CreateRecipeAsync(client, owner.Token,
            FullRequest("Суп", servings: 2, ingredients: new[] { ("Мука", 500m, "g") }));
        var brokenShare = await ShareAsync(client, owner.Token, brokenSource.Id);
        var (_, brokenImport) = await PostAuthorizedAsync<RecipeImportResultDto>(
            client, recipient.Token, $"/api/shared/{brokenShare.Token}/import", body: null);

        // Свой рецепт семьи-получателя.
        var own = await CreateRecipeAsync(client, recipient.Token,
            FullRequest("Блины", servings: 2, ingredients: new[] { ("Мука", 200m, "g") }));

        var (_, brokenDetail) = await GetAuthorizedAsync<RecipeDto>(
            client, owner.Token, $"/api/recipes/{brokenSource.Id}");
        await DeleteAuthorizedAsync(
            client, owner.Token, $"/api/recipes/{brokenSource.Id}?revision={brokenDetail!.Revision}");

        await PutAuthorizedAsync<WeekPlanDto>(client, recipient.Token,
            $"/api/plans/week/{Monday}", new SaveWeekPlanRequest(new[]
            {
                new PlanEntryRequest(0, "breakfast", own.Id, 2),
                new PlanEntryRequest(1, "lunch", warningWrapperId, 2),
                new PlanEntryRequest(2, "dinner", brokenImport!.RecipeId, 2)
            }));

        var (_, list) = await GetAuthorizedAsync<ShoppingListDto>(
            client, recipient.Token, $"/api/shopping-list?weekStart={Monday}");

        // own 200 + warning 100 = 300; удалённый источник не подставлен.
        var flour = Assert.Single(list!.Items);
        Assert.Equal("Мука", flour.Name);
        Assert.Equal(300m, flour.Amount);

        var excluded = Assert.Single(list.Excluded);
        Assert.Equal(2, excluded.Day);
        Assert.Equal("dinner", excluded.MealType);
        Assert.Equal(brokenImport.RecipeId, excluded.RecipeId);
        Assert.Equal("Суп", excluded.RecipeName);
        Assert.Equal(ShoppingListContentBuilder.SourceMissingReason, excluded.Reason);
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
        var (response, share) = await PostAuthorizedAsync<RecipeShareDto>(
            client, token, $"/api/recipes/{recipeId}/share", body: null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
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
        HttpClient client, string token, Guid recipeId, RecipeRequest request, int revision = 1)
    {
        var (response, recipe) = await PutAuthorizedAsync<RecipeDto>(
            client, token, $"/api/recipes/{recipeId}", request with { Revision = revision });
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
