using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Families;
using MenuPlanner.Api.Recipes;

namespace MenuPlanner.Api.Tests;

public sealed class RecipeMatchTests
{
    [Fact]
    public async Task Match_NoFilters_ReturnsAllFamilyRecipes()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        await CreateRecipeAsync(client, owner.Token, "Борщ", FullRequest());
        await CreateRecipeAsync(client, owner.Token, "Блины", FullRequest() with
        {
            Name = "Блины",
            Difficulty = 1,
            Calories = 200,
            Tags = new List<string> { "быстро" }
        });

        var (response, result) = await PostMatchAsync<RecipeMatchResponse>(client, owner.Token,
            new RecipeMatchRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, result!.Items.Count);
        Assert.Contains(result.Items, i => i.Name == "Борщ");
        Assert.Contains(result.Items, i => i.Name == "Блины");
    }

    [Fact]
    public async Task Match_MaxDifficulty_ExcludesHarderRecipes()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        await CreateRecipeAsync(client, owner.Token, "Борщ", FullRequest() with { Name = "Борщ", Difficulty = 3 });
        await CreateRecipeAsync(client, owner.Token, "Суп", FullRequest() with { Name = "Суп", Difficulty = 1 });

        var (_, result) = await PostMatchAsync<RecipeMatchResponse>(client, owner.Token,
            new RecipeMatchRequest(new MatchFilters(MaxDifficulty: 2)));

        var item = Assert.Single(result!.Items);
        Assert.Equal("Суп", item.Name);
    }

    [Fact]
    public async Task Match_MaxCalories_ExcludesHigherAndNullCalories()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        await CreateRecipeAsync(client, owner.Token, "Лёгкий", FullRequest() with { Name = "Лёгкий", Calories = 150 });
        await CreateRecipeAsync(client, owner.Token, "Тяжёлый", FullRequest() with { Name = "Тяжёлый", Calories = 600 });
        await CreateRecipeAsync(client, owner.Token, "Без калорий", FullRequest() with
        {
            Name = "Без калорий",
            Calories = null
        });

        var (_, result) = await PostMatchAsync<RecipeMatchResponse>(client, owner.Token,
            new RecipeMatchRequest(new MatchFilters(MaxCalories: 300)));

        var item = Assert.Single(result!.Items);
        Assert.Equal("Лёгкий", item.Name);
    }

    [Fact]
    public async Task Match_Season_ReturnsOnlyMatchingSeasons()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        await CreateRecipeAsync(client, owner.Token, "Зимнее", FullRequest() with
        {
            Name = "Зимнее",
            Seasonality = new List<string> { "winter" }
        });
        await CreateRecipeAsync(client, owner.Token, "Летнее", FullRequest() with
        {
            Name = "Летнее",
            Seasonality = new List<string> { "summer" }
        });

        var (_, result) = await PostMatchAsync<RecipeMatchResponse>(client, owner.Token,
            new RecipeMatchRequest(new MatchFilters(Seasons: new List<string> { "winter" })));

        var item = Assert.Single(result!.Items);
        Assert.Equal("Зимнее", item.Name);
    }

    [Fact]
    public async Task Match_Diet_ReturnsOnlyMatchingDiets()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        await CreateRecipeAsync(client, owner.Token, "Вегетарианское", FullRequest() with
        {
            Name = "Вегетарианское",
            Diet = new List<string> { "vegetarian" }
        });
        await CreateRecipeAsync(client, owner.Token, "Обычное", FullRequest() with { Name = "Обычное" });

        var (_, result) = await PostMatchAsync<RecipeMatchResponse>(client, owner.Token,
            new RecipeMatchRequest(new MatchFilters(Diets: new List<string> { "vegetarian" })));

        var item = Assert.Single(result!.Items);
        Assert.Equal("Вегетарианское", item.Name);
    }

    [Fact]
    public async Task Match_MaxCookTime_ExcludesLongRecipes()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        await CreateRecipeAsync(client, owner.Token, "Быстрое", FullRequest() with
        {
            Name = "Быстрое",
            CookTimeMinutes = 15
        });
        await CreateRecipeAsync(client, owner.Token, "Долгое", FullRequest() with
        {
            Name = "Долгое",
            CookTimeMinutes = 120
        });

        var (_, result) = await PostMatchAsync<RecipeMatchResponse>(client, owner.Token,
            new RecipeMatchRequest(new MatchFilters(MaxCookTimeMinutes: 30)));

        var item = Assert.Single(result!.Items);
        Assert.Equal("Быстрое", item.Name);
    }

    [Fact]
    public async Task Match_Tag_ReturnsOnlyMatchingTags()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        await CreateRecipeAsync(client, owner.Token, "Быстрое", FullRequest() with
        {
            Name = "Быстрое",
            Tags = new List<string> { "быстро" }
        });
        await CreateRecipeAsync(client, owner.Token, "Обычное", FullRequest() with { Name = "Обычное" });

        var (_, result) = await PostMatchAsync<RecipeMatchResponse>(client, owner.Token,
            new RecipeMatchRequest(new MatchFilters(Tags: new List<string> { "быстро" })));

        var item = Assert.Single(result!.Items);
        Assert.Equal("Быстрое", item.Name);
    }

    [Fact]
    public async Task Match_Ingredient_ReturnsOnlyRecipesWithThatIngredient()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        await CreateRecipeAsync(client, owner.Token, "С луком", FullRequest());
        await CreateRecipeAsync(client, owner.Token, "Без лука", FullRequest() with
        {
            Name = "Без лука",
            Ingredients = new List<RecipeIngredientRequest> { new("Картофель", 1, "kg", null) }
        });

        var (_, result) = await PostMatchAsync<RecipeMatchResponse>(client, owner.Token,
            new RecipeMatchRequest(new MatchFilters(IncludeIngredients: new List<string> { "лук" })));

        var item = Assert.Single(result!.Items);
        Assert.Equal("С луком", item.Name);
    }

    [Fact]
    public async Task Match_PreferLowCalories_SortsAscendingWithNullsLast()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        await CreateRecipeAsync(client, owner.Token, "Тяжёлый", FullRequest() with { Name = "Тяжёлый", Calories = 500 });
        await CreateRecipeAsync(client, owner.Token, "Лёгкий", FullRequest() with { Name = "Лёгкий", Calories = 100 });
        await CreateRecipeAsync(client, owner.Token, "Без калорий", FullRequest() with
        {
            Name = "Без калорий",
            Calories = null
        });

        var (_, result) = await PostMatchAsync<RecipeMatchResponse>(client, owner.Token,
            new RecipeMatchRequest(Preferences: new MatchPreferences(PreferLowCalories: true)));

        Assert.Equal(new[] { "Лёгкий", "Тяжёлый", "Без калорий" }, result!.Items.Select(i => i.Name).ToArray());
    }

    [Fact]
    public async Task Match_PreferLowComplexity_SortsAscendingByDifficulty()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        await CreateRecipeAsync(client, owner.Token, "Сложное", FullRequest() with { Name = "Сложное", Difficulty = 5 });
        await CreateRecipeAsync(client, owner.Token, "Простое", FullRequest() with { Name = "Простое", Difficulty = 1 });

        var (_, result) = await PostMatchAsync<RecipeMatchResponse>(client, owner.Token,
            new RecipeMatchRequest(Preferences: new MatchPreferences(PreferLowComplexity: true)));

        Assert.Equal(new[] { "Простое", "Сложное" }, result!.Items.Select(i => i.Name).ToArray());
    }

    [Fact]
    public async Task Match_PreferSeasons_RanksMatchingHigher()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        await CreateRecipeAsync(client, owner.Token, "Зимнее", FullRequest() with
        {
            Name = "Зимнее",
            Seasonality = new List<string> { "winter" }
        });
        await CreateRecipeAsync(client, owner.Token, "Всесезонное", FullRequest() with
        {
            Name = "Всесезонное",
            Seasonality = new List<string>() { "winter", "summer" }
        });

        var (_, result) = await PostMatchAsync<RecipeMatchResponse>(client, owner.Token,
            new RecipeMatchRequest(Preferences: new MatchPreferences(PreferSeasons: new List<string> { "winter" })));

        var winter = Assert.Single(result!.Items, i => i.Name == "Зимнее");
        var all = Assert.Single(result.Items, i => i.Name == "Всесезонное");
        Assert.Equal(1, winter.MatchScore);
        Assert.Equal(1, all.MatchScore);
    }

    [Fact]
    public async Task Match_OtherFamilyRecipes_NeverReturned()
    {
        using var client = new ApiFactory().CreateClient();
        var first = await RegisterAsync(client, "first");
        var second = await RegisterAsync(client, "second");
        await CreateFamilyAsync(client, first.Token, "Семья первая");
        await CreateFamilyAsync(client, second.Token, "Семья вторая");
        await CreateRecipeAsync(client, first.Token, "Борщ", FullRequest());

        var (response, result) = await PostMatchAsync<RecipeMatchResponse>(client, second.Token,
            new RecipeMatchRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(result!.Items);
    }

    [Fact]
    public async Task Match_WithoutFamily_ReturnsNotFound()
    {
        using var client = new ApiFactory().CreateClient();
        var lonely = await RegisterAsync(client, "lonely");

        var (response, _) = await PostMatchAsync<RecipeMatchResponse>(client, lonely.Token,
            new RecipeMatchRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("tropical")]
    [InlineData("winterr")]
    [InlineData("")]
    public async Task Match_UnknownSeason_ReturnsBadRequest(string season)
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        var (response, _) = await PostMatchAsync<RecipeErrorDto>(client, owner.Token,
            new RecipeMatchRequest(new MatchFilters(Seasons: new List<string> { season })));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Match_UnknownDietCode_Accepted()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        var (response, _) = await PostMatchAsync<RecipeMatchResponse>(client, owner.Token,
            new RecipeMatchRequest(new MatchFilters(Diets: new List<string> { "custom_diet" })));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Match_WithoutToken_ReturnsUnauthorized()
    {
        using var client = new ApiFactory().CreateClient();
        var response = await client.PostAsJsonAsync("/api/recipes/match", new RecipeMatchRequest());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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
        Ingredients: new List<RecipeIngredientRequest>
        {
            new("Лук", 1, "pcs", null)
        });

    private static async Task<RecipeDto> CreateRecipeAsync(HttpClient client, string token, string name, RecipeRequest request)
    {
        var (response, recipe) = await PostAuthorizedAsync<RecipeDto>(client, token, "/api/recipes",
            request with { Name = name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(recipe);
        return recipe;
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
