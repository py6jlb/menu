using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Families;
using MenuPlanner.Api.Recipes;

namespace MenuPlanner.Api.Tests;

/// <summary>
/// Стандартные диеты одинаково понимаются формой, подбором и чтением: известные
/// русские/кодовые варианты сводятся к каноническому коду, произвольные метки
/// сохраняются, а старые данные и живой источник остаются совместимыми.
/// </summary>
public sealed class RecipeDietTests
{
    [Fact]
    public async Task Create_WithRussianVariants_PersistsCanonicalCodes()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        var created = await CreateRecipeAsync(client, owner.Token, "Рагу", FullRequest() with
        {
            Diet = new List<string> { "Вегетарианское", "безглютеновое" }
        });

        Assert.Equal(new[] { "vegetarian", "gluten_free" }, created.Diet);

        var (_, detail) = await GetAuthorizedAsync<RecipeDto>(client, owner.Token, $"/api/recipes/{created.Id}");
        Assert.Equal(new[] { "vegetarian", "gluten_free" }, detail!.Diet);
    }

    [Fact]
    public async Task Create_Read_Edit_Match_KeepsCanonicalValue()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        var created = await CreateRecipeAsync(client, owner.Token, "Рагу", FullRequest() with
        {
            Diet = new List<string> { "Вегетарианское" }
        });
        Assert.Equal(new[] { "vegetarian" }, created.Diet);

        var (_, detail) = await GetAuthorizedAsync<RecipeDto>(client, owner.Token, $"/api/recipes/{created.Id}");
        Assert.Equal(new[] { "vegetarian" }, detail!.Diet);

        var (updateResponse, updated) = await PutAuthorizedAsync<RecipeDto>(client, owner.Token,
            $"/api/recipes/{created.Id}", FullRequest() with
            {
                Name = "Рагу",
                Diet = detail.Diet.ToList(),
                Revision = detail.Revision
            });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(new[] { "vegetarian" }, updated!.Diet);

        var (_, match) = await PostMatchAsync<RecipeMatchResponse>(client, owner.Token,
            new RecipeMatchRequest(new MatchFilters(Diets: new List<string> { "vegetarian" })));
        var item = Assert.Single(match!.Items);
        Assert.Equal("Рагу", item.Name);
        Assert.Equal(new[] { "vegetarian" }, item.Diet);
    }

    [Fact]
    public async Task SaveTwice_DoesNotDuplicateCanonicalValues()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        var created = await CreateRecipeAsync(client, owner.Token, "Рагу", FullRequest() with
        {
            Diet = new List<string> { "вегетарианское", "Вегетарианское" }
        });
        Assert.Equal(new[] { "vegetarian" }, created.Diet);

        var (_, second) = await PutAuthorizedAsync<RecipeDto>(client, owner.Token,
            $"/api/recipes/{created.Id}", FullRequest() with
            {
                Name = "Рагу",
                Diet = new List<string> { "Вегетарианское" },
                Revision = created.Revision
            });
        Assert.Equal(new[] { "vegetarian" }, second!.Diet);
    }

    [Fact]
    public async Task Create_MixedKnownAndUnknown_PreservesUnknownWithoutTranslating()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        var created = await CreateRecipeAsync(client, owner.Token, "Рагу", FullRequest() with
        {
            Diet = new List<string> { "Вегетарианское", "моя диета", "vegetarian", "постное" }
        });

        Assert.Equal(new[] { "vegetarian", "моя диета", "lean" }, created.Diet);
    }

    [Fact]
    public async Task Match_LegacyRussianValueInStorage_StillMatchesStandardFilter()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        var created = await CreateRecipeAsync(client, owner.Token, "Старое рагу", FullRequest());
        // Имитация рецепта, сохранённого до канонизации: в БД лежит русская метка.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var recipe = await db.Recipes.SingleAsync(r => r.Id == created.Id);
            recipe.Diet = new List<string> { "вегетарианское" };
            await db.SaveChangesAsync();
        }

        var (_, match) = await PostMatchAsync<RecipeMatchResponse>(client, owner.Token,
            new RecipeMatchRequest(new MatchFilters(Diets: new List<string> { "vegetarian" })));
        var item = Assert.Single(match!.Items);
        Assert.Equal("Старое рагу", item.Name);
        Assert.Equal(new[] { "vegetarian" }, item.Diet);

        var (_, detail) = await GetAuthorizedAsync<RecipeDto>(client, owner.Token, $"/api/recipes/{created.Id}");
        Assert.Equal(new[] { "vegetarian" }, detail!.Diet);
    }

    [Fact]
    public async Task Match_CustomDietLabel_IsCaseInsensitive()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        var created = await CreateRecipeAsync(client, owner.Token, "Рагу", FullRequest() with
        {
            Diet = new List<string> { "Средиземноморское" }
        });
        Assert.Equal(new[] { "Средиземноморское" }, created.Diet);

        var (_, match) = await PostMatchAsync<RecipeMatchResponse>(client, owner.Token,
            new RecipeMatchRequest(new MatchFilters(Diets: new List<string> { " средиземноморское " })));

        Assert.Equal("Рагу", Assert.Single(match!.Items).Name);
    }

    [Fact]
    public async Task Match_PreferDiets_LegacyRussianValue_RanksHigher()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        var liked = await CreateRecipeAsync(client, owner.Token, "Любимое", FullRequest());
        await CreateRecipeAsync(client, owner.Token, "Обычное", FullRequest() with { Name = "Обычное" });
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var recipe = await db.Recipes.SingleAsync(r => r.Id == liked.Id);
            recipe.Diet = new List<string> { "Вегетарианское" };
            await db.SaveChangesAsync();
        }

        var (_, match) = await PostMatchAsync<RecipeMatchResponse>(client, owner.Token,
            new RecipeMatchRequest(Preferences: new MatchPreferences(PreferDiets: new List<string> { "vegetarian" })));

        var preferred = Assert.Single(match!.Items, i => i.Name == "Любимое");
        var other = Assert.Single(match.Items, i => i.Name == "Обычное");
        Assert.Equal(1, preferred.MatchScore);
        Assert.Equal(0, other.MatchScore);
    }

    private static RecipeRequest FullRequest() => new(
        Name: "Рагу",
        Description: "Овощное рагу",
        CookTimeMinutes: 40,
        Servings: 4,
        Difficulty: 2,
        Calories: 220,
        Tags: new List<string>(),
        Seasonality: new List<string>(),
        Diet: new List<string>(),
        Steps: new List<RecipeStepRequest> { new("Потушить овощи.") },
        Ingredients: new List<RecipeIngredientRequest> { new("Кабачок", 1, "pcs", null) });

    private static async Task<RecipeDto> CreateRecipeAsync(
        HttpClient client, string token, string name, RecipeRequest request)
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
        return (response, await ReadJsonAsync<T>(response));
    }

    private static async Task<(HttpResponseMessage Response, T? Data)> PostAuthorizedAsync<T>(
        HttpClient client, string token, string path, object? body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        var response = await client.SendAsync(request);
        return (response, await ReadJsonAsync<T>(response));
    }

    private static async Task<(HttpResponseMessage Response, T? Data)> PutAuthorizedAsync<T>(
        HttpClient client, string token, string path, object? body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        var response = await client.SendAsync(request);
        return (response, await ReadJsonAsync<T>(response));
    }

    private static async Task<(HttpResponseMessage Response, T? Data)> GetAuthorizedAsync<T>(
        HttpClient client, string token, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        return (response, await ReadJsonAsync<T>(response));
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
