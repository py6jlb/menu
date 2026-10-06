using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Families;
using MenuPlanner.Api.Plans;
using MenuPlanner.Api.Recipes;
using MenuPlanner.Api.Recipes.Repetition;
using MenuPlanner.Api.Settings;

namespace MenuPlanner.Api.Tests;

public sealed class RepetitionFlowTests
{
    [Fact]
    public async Task Repetition_CountsDistinctWeeksInWindow()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var borscht = await CreateRecipeAsync(client, owner.Token, "Борщ");

        await SaveWeekAsync(client, owner.Token, CurrentMonday(), new[]
        {
            new PlanEntryRequest(0, "breakfast", borscht.Id, 4),
            new PlanEntryRequest(3, "dinner", borscht.Id, 2)
        });
        await SaveWeekAsync(client, owner.Token, CurrentMonday().AddDays(-7), new[]
        {
            new PlanEntryRequest(0, "breakfast", borscht.Id, 4)
        });
        await SaveWeekAsync(client, owner.Token, CurrentMonday().AddDays(-14), new[]
        {
            new PlanEntryRequest(0, "breakfast", borscht.Id, 4)
        });

        var (listResponse, list) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(client, owner.Token, "/api/recipes");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var summary = Assert.Single(list!);
        Assert.Equal(3, summary.RepetitionCount);

        var (detailResponse, detail) = await GetAuthorizedAsync<RecipeDto>(client, owner.Token, $"/api/recipes/{borscht.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.Equal(3, detail!.RepetitionCount);

        var (repResponse, rep) = await GetAuthorizedAsync<List<RecipeRepetitionDto>>(client, owner.Token, "/api/recipes/repetition");
        Assert.Equal(HttpStatusCode.OK, repResponse.StatusCode);
        var entry = Assert.Single(rep!);
        Assert.Equal(borscht.Id, entry.RecipeId);
        Assert.Equal(3, entry.Count);
    }

    [Fact]
    public async Task Repetition_DefaultWindow3_ExcludesOlderWeeks()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var borscht = await CreateRecipeAsync(client, owner.Token, "Борщ");

        await SaveWeekAsync(client, owner.Token, CurrentMonday(), new[]
        {
            new PlanEntryRequest(0, "breakfast", borscht.Id, 4)
        });
        await SaveWeekAsync(client, owner.Token, CurrentMonday().AddDays(-7), new[]
        {
            new PlanEntryRequest(0, "breakfast", borscht.Id, 4)
        });
        await SaveWeekAsync(client, owner.Token, CurrentMonday().AddDays(-14), new[]
        {
            new PlanEntryRequest(0, "breakfast", borscht.Id, 4)
        });
        await SaveWeekAsync(client, owner.Token, CurrentMonday().AddDays(-21), new[]
        {
            new PlanEntryRequest(0, "breakfast", borscht.Id, 4)
        });

        var (_, list) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(client, owner.Token, "/api/recipes");
        Assert.Equal(3, Assert.Single(list!).RepetitionCount);
    }

    [Fact]
    public async Task Repetition_WindowSetting1_CountsOnlyCurrentWeek()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var borscht = await CreateRecipeAsync(client, owner.Token, "Борщ");

        await SaveWeekAsync(client, owner.Token, CurrentMonday(), new[]
        {
            new PlanEntryRequest(0, "breakfast", borscht.Id, 4)
        });
        await SaveWeekAsync(client, owner.Token, CurrentMonday().AddDays(-7), new[]
        {
            new PlanEntryRequest(0, "breakfast", borscht.Id, 4)
        });

        var (updateResponse, _) = await PutAuthorizedAsync<UserSettingsDto>(client, owner.Token,
            "/api/settings", new UserSettingsRequest(1));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var (listResponse, list) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(client, owner.Token, "/api/recipes");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(1, Assert.Single(list!).RepetitionCount);
    }

    [Fact]
    public async Task Repetition_OtherFamilyPlans_DoNotCount()
    {
        using var client = new ApiFactory().CreateClient();
        var first = await RegisterAsync(client, "first");
        var second = await RegisterAsync(client, "second");
        await CreateFamilyAsync(client, first.Token, "Семья первая");
        await CreateFamilyAsync(client, second.Token, "Семья вторая");
        var borscht = await CreateRecipeAsync(client, first.Token, "Борщ");
        var cutlets = await CreateRecipeAsync(client, second.Token, "Котлеты");

        await SaveWeekAsync(client, first.Token, CurrentMonday(), new[]
        {
            new PlanEntryRequest(0, "breakfast", borscht.Id, 4)
        });
        await SaveWeekAsync(client, second.Token, CurrentMonday(), new[]
        {
            new PlanEntryRequest(0, "breakfast", cutlets.Id, 4)
        });

        var (firstListResponse, firstList) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(client, first.Token, "/api/recipes");
        Assert.Equal(HttpStatusCode.OK, firstListResponse.StatusCode);
        var firstSummary = Assert.Single(firstList!);
        Assert.Equal(borscht.Id, firstSummary.Id);
        Assert.Equal(1, firstSummary.RepetitionCount);

        var (secondListResponse, secondList) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(client, second.Token, "/api/recipes");
        Assert.Equal(HttpStatusCode.OK, secondListResponse.StatusCode);
        var secondSummary = Assert.Single(secondList!);
        Assert.Equal(cutlets.Id, secondSummary.Id);
        Assert.Equal(1, secondSummary.RepetitionCount);
    }

    [Fact]
    public async Task Repetition_RecipesWithoutPlans_HaveZeroCount()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var borscht = await CreateRecipeAsync(client, owner.Token, "Борщ");

        var (listResponse, list) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(client, owner.Token, "/api/recipes");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(0, Assert.Single(list!).RepetitionCount);

        var (repResponse, rep) = await GetAuthorizedAsync<List<RecipeRepetitionDto>>(client, owner.Token, "/api/recipes/repetition");
        Assert.Equal(HttpStatusCode.OK, repResponse.StatusCode);
        Assert.Empty(rep!);
    }

    [Fact]
    public async Task Match_Repetition_EndsAtSelectedWeek_NotServerCurrentWeek()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var borscht = await CreateRecipeAsync(client, owner.Token, "Борщ");

        // Выбранная неделя в будущем относительно текущей недели сервера.
        var selected = CurrentMonday().AddDays(7);
        await SaveWeekAsync(client, owner.Token, selected, new[] { Entry(borscht.Id) });
        await SaveWeekAsync(client, owner.Token, selected.AddDays(-7), new[] { Entry(borscht.Id) });
        await SaveWeekAsync(client, owner.Token, selected.AddDays(-14), new[] { Entry(borscht.Id) });

        // Окно из 3 недель заканчивается выбранной будущей неделей: +1, 0, −1 → 3.
        var (_, future) = await PostMatchAsync<RecipeMatchResponse>(
            client, owner.Token, new RecipeMatchRequest(WeekStart: selected));
        var futureItem = Assert.Single(future!.Items);
        Assert.Equal(3, futureItem.RepetitionCount);
        Assert.Equal(3, future.RepetitionWindowWeeks);

        // Для текущей недели сервера окно −2, −1, 0: будущей недели там нет → 2.
        var (_, current) = await PostMatchAsync<RecipeMatchResponse>(
            client, owner.Token, new RecipeMatchRequest(WeekStart: CurrentMonday()));
        Assert.Equal(2, Assert.Single(current!.Items).RepetitionCount);
    }

    [Fact]
    public async Task Match_Repetition_WindowExcludesWeeksOutsideBounds()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var borscht = await CreateRecipeAsync(client, owner.Token, "Борщ");

        var selected = new DateOnly(2026, 3, 2);
        await SaveWeekAsync(client, owner.Token, selected, new[] { Entry(borscht.Id) });
        await SaveWeekAsync(client, owner.Token, selected.AddDays(-7), new[] { Entry(borscht.Id) });
        await SaveWeekAsync(client, owner.Token, selected.AddDays(-14), new[] { Entry(borscht.Id) });
        await SaveWeekAsync(client, owner.Token, selected.AddDays(-21), new[] { Entry(borscht.Id) });

        var (_, result) = await PostMatchAsync<RecipeMatchResponse>(
            client, owner.Token, new RecipeMatchRequest(WeekStart: selected));

        // Окно из 3 недель: selected, −7, −14. Неделя −21 вне окна.
        Assert.Equal(3, Assert.Single(result!.Items).RepetitionCount);
    }

    [Fact]
    public async Task Match_Repetition_SelectedPastWeek_ExcludesLaterWeeks()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var borscht = await CreateRecipeAsync(client, owner.Token, "Борщ");

        // Выбранная неделя в прошлом: более поздние недели (в т.ч. текущая) не считаются.
        var selected = new DateOnly(2026, 3, 16);
        await SaveWeekAsync(client, owner.Token, selected.AddDays(-14), new[] { Entry(borscht.Id) });
        await SaveWeekAsync(client, owner.Token, selected, new[] { Entry(borscht.Id) });
        await SaveWeekAsync(client, owner.Token, selected.AddDays(7), new[] { Entry(borscht.Id) });
        await SaveWeekAsync(client, owner.Token, selected.AddDays(14), new[] { Entry(borscht.Id) });

        var (_, result) = await PostMatchAsync<RecipeMatchResponse>(
            client, owner.Token, new RecipeMatchRequest(WeekStart: selected));

        Assert.Equal(2, Assert.Single(result!.Items).RepetitionCount);
    }

    [Fact]
    public async Task Match_Repetition_MultipleEntriesSameWeek_CountOnce()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var borscht = await CreateRecipeAsync(client, owner.Token, "Борщ");

        var selected = new DateOnly(2026, 3, 2);
        await SaveWeekAsync(client, owner.Token, selected, new[]
        {
            new PlanEntryRequest(0, "breakfast", borscht.Id, 4),
            new PlanEntryRequest(3, "dinner", borscht.Id, 2)
        });

        var (_, result) = await PostMatchAsync<RecipeMatchResponse>(
            client, owner.Token, new RecipeMatchRequest(WeekStart: selected));

        Assert.Equal(1, Assert.Single(result!.Items).RepetitionCount);
    }

    [Fact]
    public async Task Match_Repetition_UsesPersonalWindowSetting()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var borscht = await CreateRecipeAsync(client, owner.Token, "Борщ");

        var selected = new DateOnly(2026, 4, 6);
        await SaveWeekAsync(client, owner.Token, selected, new[] { Entry(borscht.Id) });
        await SaveWeekAsync(client, owner.Token, selected.AddDays(-7), new[] { Entry(borscht.Id) });

        var (_, defaultWindow) = await PostMatchAsync<RecipeMatchResponse>(
            client, owner.Token, new RecipeMatchRequest(WeekStart: selected));
        Assert.Equal(2, Assert.Single(defaultWindow!.Items).RepetitionCount);
        Assert.Equal(3, defaultWindow.RepetitionWindowWeeks);

        var (updateResponse, _) = await PutAuthorizedAsync<UserSettingsDto>(client, owner.Token,
            "/api/settings", new UserSettingsRequest(1));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var (_, narrowWindow) = await PostMatchAsync<RecipeMatchResponse>(
            client, owner.Token, new RecipeMatchRequest(WeekStart: selected));
        Assert.Equal(1, Assert.Single(narrowWindow!.Items).RepetitionCount);
        Assert.Equal(1, narrowWindow.RepetitionWindowWeeks);
    }

    [Fact]
    public async Task Match_Repetition_NoSavedPlan_IsZero()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        await CreateRecipeAsync(client, owner.Token, "Борщ");

        var (_, result) = await PostMatchAsync<RecipeMatchResponse>(
            client, owner.Token, new RecipeMatchRequest(WeekStart: new DateOnly(2026, 3, 2)));

        Assert.Equal(0, Assert.Single(result!.Items).RepetitionCount);
    }

    [Fact]
    public async Task Match_Repetition_OtherFamilyPlans_DoNotCount()
    {
        using var client = new ApiFactory().CreateClient();
        var first = await RegisterAsync(client, "first");
        var second = await RegisterAsync(client, "second");
        await CreateFamilyAsync(client, first.Token, "Семья первая");
        await CreateFamilyAsync(client, second.Token, "Семья вторая");
        var borscht = await CreateRecipeAsync(client, first.Token, "Борщ");
        await CreateRecipeAsync(client, second.Token, "Котлеты");

        var selected = new DateOnly(2026, 3, 2);
        await SaveWeekAsync(client, first.Token, selected, new[] { Entry(borscht.Id) });

        // Во второй семье планов нет: чужая история не подмешивается в подбор.
        var (_, secondResult) = await PostMatchAsync<RecipeMatchResponse>(
            client, second.Token, new RecipeMatchRequest(WeekStart: selected));
        var secondItem = Assert.Single(secondResult!.Items);
        Assert.Equal("Котлеты", secondItem.Name);
        Assert.Equal(0, secondItem.RepetitionCount);

        var (_, firstResult) = await PostMatchAsync<RecipeMatchResponse>(
            client, first.Token, new RecipeMatchRequest(WeekStart: selected));
        Assert.Equal(1, Assert.Single(firstResult!.Items).RepetitionCount);
    }

    [Fact]
    public async Task Settings_GetReturnsDefaultAndRoundTripsUpdate()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        var (getResponse, settings) = await GetAuthorizedAsync<UserSettingsDto>(client, owner.Token, "/api/settings");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(3, settings!.RepetitionWindowWeeks);

        var (updateResponse, updated) = await PutAuthorizedAsync<UserSettingsDto>(client, owner.Token,
            "/api/settings", new UserSettingsRequest(10));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(10, updated!.RepetitionWindowWeeks);

        var (getAgainResponse, again) = await GetAuthorizedAsync<UserSettingsDto>(client, owner.Token, "/api/settings");
        Assert.Equal(HttpStatusCode.OK, getAgainResponse.StatusCode);
        Assert.Equal(10, again!.RepetitionWindowWeeks);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(53)]
    [InlineData(null)]
    public async Task Settings_InvalidWindow_ReturnsBadRequest(int? weeks)
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        var (response, error) = await PutAuthorizedAsync<SettingsErrorDto>(client, owner.Token,
            "/api/settings", new UserSettingsRequest(weeks));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(error);
        Assert.False(string.IsNullOrWhiteSpace(error.Error));
    }

    [Fact]
    public async Task Settings_ArePerUser()
    {
        using var client = new ApiFactory().CreateClient();
        var first = await RegisterAsync(client, "first");
        var second = await RegisterAsync(client, "second");

        var (firstGet, firstSettings) = await GetAuthorizedAsync<UserSettingsDto>(client, first.Token, "/api/settings");
        Assert.Equal(HttpStatusCode.OK, firstGet.StatusCode);
        Assert.Equal(3, firstSettings!.RepetitionWindowWeeks);

        var (updateResponse, _) = await PutAuthorizedAsync<UserSettingsDto>(client, first.Token,
            "/api/settings", new UserSettingsRequest(12));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var (secondGet, secondSettings) = await GetAuthorizedAsync<UserSettingsDto>(client, second.Token, "/api/settings");
        Assert.Equal(HttpStatusCode.OK, secondGet.StatusCode);
        Assert.Equal(3, secondSettings!.RepetitionWindowWeeks);
    }

    [Fact]
    public async Task Endpoints_WithoutToken_ReturnUnauthorized()
    {
        using var client = new ApiFactory().CreateClient();

        var getSettings = await client.GetAsync("/api/settings");
        Assert.Equal(HttpStatusCode.Unauthorized, getSettings.StatusCode);

        var putSettings = await client.PutAsJsonAsync("/api/settings", new UserSettingsRequest(5));
        Assert.Equal(HttpStatusCode.Unauthorized, putSettings.StatusCode);

        var repetition = await client.GetAsync("/api/recipes/repetition");
        Assert.Equal(HttpStatusCode.Unauthorized, repetition.StatusCode);
    }

    private static DateOnly CurrentMonday()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var offset = ((int)today.DayOfWeek + 6) % 7;
        return today.AddDays(-offset);
    }

    private static PlanEntryRequest Entry(Guid recipeId) => new(0, "breakfast", recipeId, 4);

    private static async Task SaveWeekAsync(
        HttpClient client, string token, DateOnly monday, IReadOnlyList<PlanEntryRequest> entries)
    {
        var (response, _) = await PutAuthorizedAsync<WeekPlanDto>(client, token,
            $"/api/plans/week/{monday.ToString("yyyy-MM-dd")}", new SaveWeekPlanRequest(entries));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<RecipeDto> CreateRecipeAsync(HttpClient client, string token, string name)
    {
        var (response, recipe) = await PostAuthorizedAsync<RecipeDto>(client, token, "/api/recipes",
            FullRecipeRequest() with { Name = name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(recipe);
        return recipe;
    }

    private static RecipeRequest FullRecipeRequest() => new(
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
        Ingredients: new List<RecipeIngredientRequest>());

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

    private static Task<(HttpResponseMessage Response, T? Data)> PostMatchAsync<T>(
        HttpClient client, string token, object? body) =>
        PostAuthorizedAsync<T>(client, token, "/api/recipes/match", body ?? new RecipeMatchRequest());

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