using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Families;
using MenuPlanner.Api.Recipes;

namespace MenuPlanner.Api.Tests;

/// <summary>
/// HTTP-поведение валидации рецепта: понятная ошибка у поля вместо 500, точное
/// сохранение количества и безопасный отказ на malformed/null данные.
/// </summary>
public sealed class RecipeValidationFlowTests
{
    [Fact]
    public async Task Amount_WithHiddenPrecision_ReturnsFieldError()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        var (response, error) = await PostAuthorizedAsync<RecipeValidationErrorDto>(
            client, owner.Token, "/api/recipes", Request(amount: 0.001m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal("ingredient_amount_precision", error!.Code);
        Assert.Equal("ingredients[0].amount", error.Field);
        Assert.Contains("двух знаков", error.Error);
    }

    [Fact]
    public async Task Amount_AboveStorageRange_ReturnsFieldError()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        var (response, error) = await PostAuthorizedAsync<RecipeValidationErrorDto>(
            client, owner.Token, "/api/recipes", Request(amount: 100_000_000m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ingredient_amount_range", error!.Code);
        Assert.Equal("ingredients[0].amount", error.Field);
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("1.50")]
    [InlineData("99999999.99")]
    public async Task AcceptedAmount_RoundTripsUnchanged(string raw)
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var amount = decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);

        var (createdResponse, created) = await PostAuthorizedAsync<RecipeDto>(
            client, owner.Token, "/api/recipes", Request(amount: amount));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);

        var (getResponse, detail) = await GetAuthorizedAsync<RecipeDto>(
            client, owner.Token, $"/api/recipes/{created!.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(amount, detail!.Ingredients.Single().Amount);
    }

    [Fact]
    public async Task AcceptedAmount_SurvivesUpdateRoundTrip()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        var (_, created) = await PostAuthorizedAsync<RecipeDto>(
            client, owner.Token, "/api/recipes", Request(amount: 1m));

        var (updateResponse, updated) = await PutAuthorizedAsync<RecipeDto>(
            client, owner.Token, $"/api/recipes/{created!.Id}",
            Request(amount: 99999999.99m) with { Revision = created.Revision });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var (_, detail) = await GetAuthorizedAsync<RecipeDto>(
            client, owner.Token, $"/api/recipes/{created.Id}");
        Assert.Equal(99999999.99m, detail!.Ingredients.Single().Amount);
    }

    [Fact]
    public async Task FamilyName_AboveStorageLimit_ReturnsFieldError()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");

        var (response, error) = await PostAuthorizedAsync<FamilyErrorDto>(
            client, owner.Token, "/api/families", new { name = new string('С', 201) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("family_name_too_long", error!.Code);
        Assert.Equal("name", error.Field);
    }

    [Fact]
    public async Task Email_AboveStorageLimit_IsRejected()
    {
        using var client = new ApiFactory().CreateClient();

        var localPart = new string('a', 310);
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { email = $"{localPart}@example.com", password = "secret1" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NullCollectionElements_AreIgnoredNotFatal()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        var body = new
        {
            name = "Борщ",
            description = "Описание",
            cookTimeMinutes = 90,
            servings = 6,
            difficulty = 3,
            calories = 350,
            tags = new object?[] { null, "суп" },
            seasonality = new object?[] { null, "winter" },
            diet = new object?[] { null, "vegetarian" },
            steps = new object?[] { null, new { text = "Шаг" } },
            ingredients = new object?[] { null, new { name = "Свёкла", amount = 2m, unit = "pcs", note = (string?)null } }
        };

        var (response, created) = await PostAuthorizedAsync<RecipeDto>(client, owner.Token, "/api/recipes", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(created);
        Assert.Equal(new[] { "суп" }, created!.Tags);
        Assert.Single(created.Ingredients);
    }

    [Fact]
    public async Task MalformedJson_ReturnsControlledBadRequest()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/recipes");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", owner.Token);
        request.Content = new StringContent("{ не json", Encoding.UTF8, "application/json");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("System.", body);
        Assert.DoesNotContain("Npgsql", body);
    }

    [Fact]
    public async Task UnexpectedFailure_ReturnsInternalError_WithoutLeaks_WithTraceId()
    {
        using var factory = new ApiFactory();
        factory.Interceptors.Add(new ThrowingInterceptor());
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        var (response, error) = await PostAuthorizedAsync<ApiErrorProbeDto>(
            client, owner.Token, "/api/recipes", Request(amount: 2m));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal("internal_error", error!.Code);
        Assert.False(string.IsNullOrWhiteSpace(error.TraceId));
        Assert.DoesNotContain("InvalidOperationException", error.Error);
        Assert.DoesNotContain("SQL", error.Error);
    }

    private sealed record ApiErrorProbeDto(string Error, string Code, string? TraceId);

    private sealed class ThrowingInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            // Регистрация и семья сохраняются штатно; падает только запись рецепта.
            if (eventData.Context?.ChangeTracker.Entries<Recipe>().Any(e => e.State == EntityState.Added) == true)
                throw new InvalidOperationException("Секретный сбой хранилища: SQL detail.");

            return ValueTask.FromResult(result);
        }
    }

    private static RecipeRequest Request(decimal amount) => new(
        Name: "Борщ",
        Description: "Описание",
        CookTimeMinutes: 90,
        Servings: 6,
        Difficulty: 3,
        Calories: 350,
        Tags: new List<string> { "суп" },
        Seasonality: new List<string> { "winter" },
        Diet: new List<string>(),
        Steps: new List<RecipeStepRequest> { new("Шаг") },
        Ingredients: new List<RecipeIngredientRequest> { new("Свёкла", amount, "pcs", null) });

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "secret1" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
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
