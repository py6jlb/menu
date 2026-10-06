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

public sealed class ShoppingListFlowTests
{
    private const string Monday = "2026-09-07";

    [Fact]
    public async Task Get_WithoutPlan_ReturnsEmptyItems()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        var (response, list) = await GetAuthorizedAsync<ShoppingListDto>(client, owner.Token,
            $"/api/shopping-list?weekStart={Monday}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Monday, list!.WeekStart);
        Assert.False(list.HasPlan);
        Assert.Empty(list.Items);
        Assert.Empty(list.Excluded);
    }

    [Fact]
    public async Task Get_WithEmptyPlan_ReportsExistingPlanWithoutItems()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        var put = await PutAuthorizedAsync<WeekPlanDto>(client, owner.Token,
            $"/api/plans/week/{Monday}", new SaveWeekPlanRequest(Array.Empty<PlanEntryRequest>()));
        Assert.Equal(HttpStatusCode.OK, put.Response.StatusCode);

        var (response, list) = await GetAuthorizedAsync<ShoppingListDto>(client, owner.Token,
            $"/api/shopping-list?weekStart={Monday}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(list!.HasPlan);
        Assert.Empty(list.Items);
        Assert.Empty(list.Excluded);
    }

    [Fact]
    public async Task Get_AggregatesScalesAndConvertsIngredients()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        var pancakes = await CreateRecipeAsync(client, owner.Token, "Блины", servings: 4,
            ("мука", 300m, "g"),
            ("молоко", 300m, "ml"),
            ("яйцо", 2m, "pcs"),
            ("соль", 1m, "tsp"));

        var pancakeStack = await CreateRecipeAsync(client, owner.Token, "Панкейки", servings: 2,
            ("мука", 150m, "g"),
            ("молоко", 1m, "l"),
            ("яйцо", 1m, "pcs"),
            ("соль", 1m, "tbsp"));

        var put = await PutAuthorizedAsync<WeekPlanDto>(client, owner.Token,
            $"/api/plans/week/{Monday}", new SaveWeekPlanRequest(new[]
            {
                new PlanEntryRequest(0, "breakfast", pancakes.Id, 8),
                new PlanEntryRequest(1, "lunch", pancakeStack.Id, 2)
            }));
        Assert.Equal(HttpStatusCode.OK, put.Response.StatusCode);

        var (response, list) = await GetAuthorizedAsync<ShoppingListDto>(client, owner.Token,
            $"/api/shopping-list?weekStart={Monday}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var items = list!.Items;
        Assert.Equal(5, items.Count);

        var flour = Assert.Single(items, i => i.Name == "мука");
        Assert.Equal(750m, flour.Amount);
        Assert.Equal("g", flour.Unit);
        Assert.Equal("750 г", flour.Display);

        var milk = Assert.Single(items, i => i.Name == "молоко");
        Assert.Equal(1.6m, milk.Amount);
        Assert.Equal("l", milk.Unit);
        Assert.Equal("1.6 л", milk.Display);

        var eggs = Assert.Single(items, i => i.Name == "яйцо");
        Assert.Equal(5m, eggs.Amount);
        Assert.Equal("pcs", eggs.Unit);
        Assert.Equal("5 шт", eggs.Display);

        var saltTsp = Assert.Single(items, i => i.Name == "соль" && i.Unit == "tsp");
        Assert.Equal(2m, saltTsp.Amount);
        Assert.Equal("2 ч. ложки", saltTsp.Display);

        var saltTbsp = Assert.Single(items, i => i.Name == "соль" && i.Unit == "tbsp");
        Assert.Equal("1 ст. ложка", saltTbsp.Display);
    }

    [Fact]
    public async Task Get_ScalesByPortionsOverServings()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        var omelette = await CreateRecipeAsync(client, owner.Token, "Омлет", servings: 4,
            ("яйцо", 2m, "pcs"),
            ("молоко", 100m, "ml"));

        var put = await PutAuthorizedAsync<WeekPlanDto>(client, owner.Token,
            $"/api/plans/week/{Monday}", new SaveWeekPlanRequest(new[]
            {
                new PlanEntryRequest(0, "breakfast", omelette.Id, 8)
            }));
        Assert.Equal(HttpStatusCode.OK, put.Response.StatusCode);

        var (response, list) = await GetAuthorizedAsync<ShoppingListDto>(client, owner.Token,
            $"/api/shopping-list?weekStart={Monday}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var eggs = Assert.Single(list!.Items, i => i.Name == "яйцо");
        Assert.Equal(4m, eggs.Amount);
        Assert.Equal("4 шт", eggs.Display);

        var milk = Assert.Single(list.Items, i => i.Name == "молоко");
        Assert.Equal(200m, milk.Amount);
        Assert.Equal("200 мл", milk.Display);
    }

    [Fact]
    public async Task Get_IsScopedPerFamily()
    {
        using var client = new ApiFactory().CreateClient();
        var first = await RegisterAsync(client, "first");
        var second = await RegisterAsync(client, "second");
        await CreateFamilyAsync(client, first.Token, "Семья первая");
        await CreateFamilyAsync(client, second.Token, "Семья вторая");

        var borscht = await CreateRecipeAsync(client, first.Token, "Борщ", servings: 4,
            ("мука", 300m, "g"));

        var put = await PutAuthorizedAsync<WeekPlanDto>(client, first.Token,
            $"/api/plans/week/{Monday}", new SaveWeekPlanRequest(new[]
            {
                new PlanEntryRequest(0, "breakfast", borscht.Id, 4)
            }));
        Assert.Equal(HttpStatusCode.OK, put.Response.StatusCode);

        var (secondResponse, secondList) = await GetAuthorizedAsync<ShoppingListDto>(client, second.Token,
            $"/api/shopping-list?weekStart={Monday}");
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Empty(secondList!.Items);

        var (firstResponse, firstList) = await GetAuthorizedAsync<ShoppingListDto>(client, first.Token,
            $"/api/shopping-list?weekStart={Monday}");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Single(firstList!.Items);
    }

    [Fact]
    public async Task Get_WithoutFamily_ReturnsNotFound()
    {
        using var client = new ApiFactory().CreateClient();
        var lonely = await RegisterAsync(client, "lonely");

        var (response, _) = await GetAuthorizedAsync<ShoppingListErrorDto>(client, lonely.Token,
            $"/api/shopping-list?weekStart={Monday}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_WithNonMondayWeekStart_ReturnsBadRequest()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        var (response, _) = await GetAuthorizedAsync<ShoppingListErrorDto>(client, owner.Token,
            "/api/shopping-list?weekStart=2026-09-08");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_WithoutToken_ReturnsUnauthorized()
    {
        using var client = new ApiFactory().CreateClient();

        var response = await client.GetAsync($"/api/shopping-list?weekStart={Monday}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<RecipeDto> CreateRecipeAsync(
        HttpClient client, string token, string name, int servings,
        params (string Name, decimal Amount, string Unit)[] ingredients)
    {
        var request = new RecipeRequest(
            Name: name,
            Description: null,
            CookTimeMinutes: 30,
            Servings: servings,
            Difficulty: 2,
            Calories: null,
            Tags: new List<string>(),
            Seasonality: new List<string>(),
            Diet: new List<string>(),
            Steps: new List<RecipeStepRequest> { new("Приготовить.") },
            Ingredients: ingredients
                .Select(i => new RecipeIngredientRequest(i.Name, i.Amount, i.Unit, null))
                .ToList());

        var (response, recipe) = await PostAuthorizedAsync<RecipeDto>(client, token, "/api/recipes", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
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