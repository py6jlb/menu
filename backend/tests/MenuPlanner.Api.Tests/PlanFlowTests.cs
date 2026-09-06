using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Families;
using MenuPlanner.Api.Plans;
using MenuPlanner.Api.Recipes;

namespace MenuPlanner.Api.Tests;

public sealed class PlanFlowTests
{
    private const string Monday = "2026-09-07";

    [Fact]
    public async Task Put_ThenGet_RoundTripsEntries_WithRecipeNames()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var borscht = await CreateRecipeAsync(client, owner.Token, "Борщ");
        var pancakes = await CreateRecipeAsync(client, owner.Token, "Блины");

        var put = await PutAuthorizedAsync<WeekPlanDto>(client, owner.Token,
            $"/api/plans/week/{Monday}", new SaveWeekPlanRequest(new[]
            {
                new PlanEntryRequest(0, "breakfast", borscht.Id, 4),
                new PlanEntryRequest(3, "dinner", pancakes.Id, 2)
            }));
        Assert.Equal(HttpStatusCode.OK, put.Response.StatusCode);
        Assert.NotNull(put.Data);
        Assert.Equal(2, put.Data.Entries.Count);

        var (getResponse, plan) = await GetAuthorizedAsync<WeekPlanDto>(client, owner.Token,
            $"/api/plans/week/{Monday}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(Monday, plan!.WeekStart);
        Assert.Equal(2, plan.Entries.Count);

        var breakfast = Assert.Single(plan.Entries, e => e.Day == 0 && e.MealType == "breakfast");
        Assert.Equal(borscht.Id, breakfast.RecipeId);
        Assert.Equal("Борщ", breakfast.RecipeName);
        Assert.Equal(4, breakfast.Portions);

        var dinner = Assert.Single(plan.Entries, e => e.Day == 3 && e.MealType == "dinner");
        Assert.Equal("Блины", dinner.RecipeName);
        Assert.Equal(2, dinner.Portions);
    }

    [Fact]
    public async Task Put_UpsertReplacesPriorEntriesForTheWeek()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var borscht = await CreateRecipeAsync(client, owner.Token, "Борщ");
        var soup = await CreateRecipeAsync(client, owner.Token, "Суп");
        var pancakes = await CreateRecipeAsync(client, owner.Token, "Блины");

        var first = await PutAuthorizedAsync<WeekPlanDto>(client, owner.Token,
            $"/api/plans/week/{Monday}", new SaveWeekPlanRequest(new[]
            {
                new PlanEntryRequest(0, "breakfast", borscht.Id, 4)
            }));
        Assert.Equal(HttpStatusCode.OK, first.Response.StatusCode);

        var second = await PutAuthorizedAsync<WeekPlanDto>(client, owner.Token,
            $"/api/plans/week/{Monday}", new SaveWeekPlanRequest(new[]
            {
                new PlanEntryRequest(0, "breakfast", soup.Id, 5),
                new PlanEntryRequest(1, "lunch", pancakes.Id, 3)
            }));
        Assert.Equal(HttpStatusCode.OK, second.Response.StatusCode);
        Assert.Equal(2, second.Data!.Entries.Count);

        var (getResponse, plan) = await GetAuthorizedAsync<WeekPlanDto>(client, owner.Token,
            $"/api/plans/week/{Monday}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(2, plan!.Entries.Count);

        var breakfast = Assert.Single(plan.Entries, e => e.Day == 0 && e.MealType == "breakfast");
        Assert.Equal(soup.Id, breakfast.RecipeId);
        Assert.Equal("Суп", breakfast.RecipeName);
        Assert.Equal(5, breakfast.Portions);

        var lunch = Assert.Single(plan.Entries, e => e.Day == 1 && e.MealType == "lunch");
        Assert.Equal("Блины", lunch.RecipeName);
    }

    [Fact]
    public async Task Get_ForWeekWithoutPlan_ReturnsEmptyEntries()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        var (response, plan) = await GetAuthorizedAsync<WeekPlanDto>(client, owner.Token,
            $"/api/plans/week/{Monday}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(plan);
        Assert.Equal(Monday, plan.WeekStart);
        Assert.Empty(plan.Entries);
    }

    [Theory]
    [InlineData(-1, "breakfast", 1)]
    [InlineData(7, "breakfast", 1)]
    [InlineData(0, "brunch", 1)]
    [InlineData(0, "breakfast", 0)]
    public async Task Put_InvalidEntry_ReturnsBadRequest(int day, string mealType, int portions)
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var borscht = await CreateRecipeAsync(client, owner.Token, "Борщ");

        var (response, _) = await PutAuthorizedAsync<PlanErrorDto>(client, owner.Token,
            $"/api/plans/week/{Monday}", new SaveWeekPlanRequest(new[]
            {
                new PlanEntryRequest(day, mealType, borscht.Id, portions)
            }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_DuplicateSlotInRequest_ReturnsBadRequest()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var borscht = await CreateRecipeAsync(client, owner.Token, "Борщ");

        var (response, _) = await PutAuthorizedAsync<PlanErrorDto>(client, owner.Token,
            $"/api/plans/week/{Monday}", new SaveWeekPlanRequest(new[]
            {
                new PlanEntryRequest(0, "breakfast", borscht.Id, 4),
                new PlanEntryRequest(0, "breakfast", borscht.Id, 2)
            }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_RecipeFromAnotherFamily_ReturnsBadRequest()
    {
        using var client = new ApiFactory().CreateClient();
        var first = await RegisterAsync(client, "first");
        var second = await RegisterAsync(client, "second");
        await CreateFamilyAsync(client, first.Token, "Семья первая");
        await CreateFamilyAsync(client, second.Token, "Семья вторая");
        var borscht = await CreateRecipeAsync(client, first.Token, "Борщ");

        var (response, _) = await PutAuthorizedAsync<PlanErrorDto>(client, second.Token,
            $"/api/plans/week/{Monday}", new SaveWeekPlanRequest(new[]
            {
                new PlanEntryRequest(0, "breakfast", borscht.Id, 4)
            }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Plan_IsScopedPerFamilyPerWeek()
    {
        using var client = new ApiFactory().CreateClient();
        var first = await RegisterAsync(client, "first");
        var second = await RegisterAsync(client, "second");
        await CreateFamilyAsync(client, first.Token, "Семья первая");
        await CreateFamilyAsync(client, second.Token, "Семья вторая");
        var borscht = await CreateRecipeAsync(client, first.Token, "Борщ");
        var cutlets = await CreateRecipeAsync(client, second.Token, "Котлеты");

        var firstPut = await PutAuthorizedAsync<WeekPlanDto>(client, first.Token,
            $"/api/plans/week/{Monday}", new SaveWeekPlanRequest(new[]
            {
                new PlanEntryRequest(0, "breakfast", borscht.Id, 4)
            }));
        Assert.Equal(HttpStatusCode.OK, firstPut.Response.StatusCode);

        var (secondGet, secondPlan) = await GetAuthorizedAsync<WeekPlanDto>(client, second.Token,
            $"/api/plans/week/{Monday}");
        Assert.Equal(HttpStatusCode.OK, secondGet.StatusCode);
        Assert.Empty(secondPlan!.Entries);

        var secondPut = await PutAuthorizedAsync<WeekPlanDto>(client, second.Token,
            $"/api/plans/week/{Monday}", new SaveWeekPlanRequest(new[]
            {
                new PlanEntryRequest(0, "breakfast", cutlets.Id, 6)
            }));
        Assert.Equal(HttpStatusCode.OK, secondPut.Response.StatusCode);

        var (firstGet, firstPlan) = await GetAuthorizedAsync<WeekPlanDto>(client, first.Token,
            $"/api/plans/week/{Monday}");
        Assert.Equal(HttpStatusCode.OK, firstGet.StatusCode);
        var breakfast = Assert.Single(firstPlan!.Entries);
        Assert.Equal(borscht.Id, breakfast.RecipeId);
    }

    [Fact]
    public async Task FamilyMember_CanSaveAndViewTheWeekPlan()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        var member = await RegisterAsync(client, "member");
        var (_, family) = await PostAuthorizedAsync<FamilyDto>(client, owner.Token, "/api/families",
            new { name = "Семья" });
        await PostAuthorizedAsync<FamilyDto>(client, member.Token, "/api/families/join",
            new { inviteCode = family!.InviteCode });
        var borscht = await CreateRecipeAsync(client, owner.Token, "Борщ");

        var (putResponse, _) = await PutAuthorizedAsync<WeekPlanDto>(client, member.Token,
            $"/api/plans/week/{Monday}", new SaveWeekPlanRequest(new[]
            {
                new PlanEntryRequest(0, "breakfast", borscht.Id, 4)
            }));
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var (getResponse, plan) = await GetAuthorizedAsync<WeekPlanDto>(client, member.Token,
            $"/api/plans/week/{Monday}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Single(plan!.Entries);
    }

    [Fact]
    public async Task Endpoints_WithoutFamily_ReturnNotFound()
    {
        using var client = new ApiFactory().CreateClient();
        var lonely = await RegisterAsync(client, "lonely");

        var (getResponse, _) = await GetAuthorizedAsync<WeekPlanDto>(client, lonely.Token,
            $"/api/plans/week/{Monday}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        var (putResponse, _) = await PutAuthorizedAsync<WeekPlanDto>(client, lonely.Token,
            $"/api/plans/week/{Monday}", new SaveWeekPlanRequest(new[] { new PlanEntryRequest(0, "breakfast", Guid.NewGuid(), 1) }));
        Assert.Equal(HttpStatusCode.NotFound, putResponse.StatusCode);
    }

    [Fact]
    public async Task Endpoints_WithNonMondayWeekStart_ReturnBadRequest()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var borscht = await CreateRecipeAsync(client, owner.Token, "Борщ");

        var (getResponse, _) = await GetAuthorizedAsync<WeekPlanDto>(client, owner.Token,
            "/api/plans/week/2026-09-08");
        Assert.Equal(HttpStatusCode.BadRequest, getResponse.StatusCode);

        var (putResponse, _) = await PutAuthorizedAsync<WeekPlanDto>(client, owner.Token,
            "/api/plans/week/2026-09-08", new SaveWeekPlanRequest(new[]
            {
                new PlanEntryRequest(0, "breakfast", borscht.Id, 1)
            }));
        Assert.Equal(HttpStatusCode.BadRequest, putResponse.StatusCode);
    }

    [Fact]
    public async Task Endpoints_WithoutToken_ReturnUnauthorized()
    {
        using var client = new ApiFactory().CreateClient();

        var get = await client.GetAsync($"/api/plans/week/{Monday}");
        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);

        var put = await client.PutAsJsonAsync($"/api/plans/week/{Monday}",
            new SaveWeekPlanRequest(new[] { new PlanEntryRequest(0, "breakfast", Guid.NewGuid(), 1) }));
        Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
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