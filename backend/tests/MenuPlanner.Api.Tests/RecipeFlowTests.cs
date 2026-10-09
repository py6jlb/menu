using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Families;
using MenuPlanner.Api.Recipes;

namespace MenuPlanner.Api.Tests;

public sealed class RecipeFlowTests
{
    [Fact]
    public async Task Create_List_Get_Update_Delete_HappyPath()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        var createRequest = FullRequest();
        var (createdResponse, created) = await PostAuthorizedAsync<RecipeDto>(client, owner.Token,
            "/api/recipes", createRequest);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        Assert.NotNull(created);
        Assert.Equal("Борщ", created.Name);
        Assert.Equal("Классический борщ", created.Description);
        Assert.Equal(90, created.CookTimeMinutes);
        Assert.Equal(6, created.Servings);
        Assert.Equal(3, created.Difficulty);
        Assert.Equal(350, created.Calories);
        Assert.Equal(new[] { "суп", "первое" }, created.Tags);
        Assert.Equal(new[] { "winter", "autumn" }, created.Seasonality);
        Assert.Empty(created.Diet);
        Assert.Equal(new[] { "Сварить бульон.", "Добавить свёклу." }, created.Steps);
        Assert.Equal(2, created.Ingredients.Count);
        Assert.Equal("Свёкла", created.Ingredients[0].Name);
        Assert.Equal(2m, created.Ingredients[0].Amount);
        Assert.Equal("pcs", created.Ingredients[0].Unit);
        Assert.Equal("Соль", created.Ingredients[1].Name);
        Assert.Equal("по вкусу", created.Ingredients[1].Note);

        var (listResponse, list) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(client, owner.Token, "/api/recipes");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var summary = Assert.Single(list!);
        Assert.Equal(created.Id, summary.Id);
        Assert.Equal("Борщ", summary.Name);

        var (getResponse, detail) = await GetAuthorizedAsync<RecipeDto>(client, owner.Token, $"/api/recipes/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(created.Id, detail!.Id);
        Assert.Equal(created.Steps, detail.Steps);

        var updateRequest = createRequest with
        {
            Name = "Борщ по-домашнему",
            Difficulty = 4,
            Revision = created.Revision,
            Ingredients = new List<RecipeIngredientRequest>
            {
                new("Свёкла", 3, "pcs", "средняя"),
                new("Картофель", 0.5m, "kg", null),
                new("Соль", 0.5m, "tsp", "по вкусу")
            }
        };
        var (updateResponse, updated) = await PutAuthorizedAsync<RecipeDto>(client, owner.Token,
            $"/api/recipes/{created.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal("Борщ по-домашнему", updated!.Name);
        Assert.Equal(4, updated.Difficulty);
        Assert.Equal(3, updated.Ingredients.Count);
        Assert.Equal(3m, updated.Ingredients[0].Amount);

        var deleteResponse = await DeleteAuthorizedAsync(
            client, owner.Token, $"/api/recipes/{created.Id}?revision={updated.Revision}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var (afterDelete, _) = await GetAuthorizedAsync<RecipeDto>(client, owner.Token, $"/api/recipes/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task FamilyMember_CanEditAndDelete_Recipe()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        var member = await RegisterAsync(client, "member");
        var (_, family) = await PostAuthorizedAsync<FamilyDto>(client, owner.Token, "/api/families",
            new { name = "Семья" });
        await PostAuthorizedAsync<FamilyDto>(client, member.Token, "/api/families/join",
            new { inviteCode = family!.InviteCode });

        var (createdResponse, created) = await PostAuthorizedAsync<RecipeDto>(client, owner.Token,
            "/api/recipes", FullRequest());
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);

        var (getResponse, _) = await GetAuthorizedAsync<RecipeDto>(client, member.Token, $"/api/recipes/{created!.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var (updateResponse, updated) = await PutAuthorizedAsync<RecipeDto>(client, member.Token,
            $"/api/recipes/{created.Id}", FullRequest() with { Name = "Борщ от участника", Revision = created.Revision });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var deleteResponse = await DeleteAuthorizedAsync(
            client, member.Token, $"/api/recipes/{created.Id}?revision={updated!.Revision}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Recipe_FromAnotherFamily_IsNotVisible()
    {
        using var client = new ApiFactory().CreateClient();
        var first = await RegisterAsync(client, "first");
        var second = await RegisterAsync(client, "second");
        await CreateFamilyAsync(client, first.Token, "Семья первая");
        await CreateFamilyAsync(client, second.Token, "Семья вторая");

        var (createdResponse, created) = await PostAuthorizedAsync<RecipeDto>(client, first.Token,
            "/api/recipes", FullRequest());
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);

        var (listResponse, list) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(client, second.Token, "/api/recipes");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Empty(list!);

        var (getResponse, _) = await GetAuthorizedAsync<RecipeDto>(client, second.Token, $"/api/recipes/{created!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        var (updateResponse, _) = await PutAuthorizedAsync<RecipeDto>(client, second.Token,
            $"/api/recipes/{created.Id}", FullRequest());
        Assert.Equal(HttpStatusCode.NotFound, updateResponse.StatusCode);

        var deleteResponse = await DeleteAuthorizedAsync(client, second.Token, $"/api/recipes/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutFamily_ReturnsNotFound_ListReturnsEmpty()
    {
        using var client = new ApiFactory().CreateClient();
        var user = await RegisterAsync(client, "lonely");

        var (createResponse, _) = await PostAuthorizedAsync<RecipeDto>(client, user.Token,
            "/api/recipes", FullRequest());
        Assert.Equal(HttpStatusCode.NotFound, createResponse.StatusCode);

        var (listResponse, list) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(client, user.Token, "/api/recipes");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Empty(list!);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("unit")]
    [InlineData("difficulty")]
    [InlineData("amount")]
    public async Task ValidationFailures_ReturnBadRequest(string field)
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        object body = field switch
        {
            "name" => FullRequest() with { Name = null },
            "unit" => FullRequest() with
            {
                Ingredients = new List<RecipeIngredientRequest> { new("Соль", 0.5m, "bucket", null) }
            },
            "difficulty" => FullRequest() with { Difficulty = 9 },
            "amount" => FullRequest() with
            {
                Ingredients = new List<RecipeIngredientRequest> { new("Соль", -1m, "tsp", null) }
            },
            _ => FullRequest()
        };

        var (response, _) = await PostAuthorizedAsync<RecipeErrorDto>(client, owner.Token, "/api/recipes", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ValidationError_ContainsRussianMessage()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        var (response, error) = await PostAuthorizedAsync<RecipeErrorDto>(client, owner.Token, "/api/recipes",
            FullRequest() with { Name = null });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(error);
        Assert.False(string.IsNullOrWhiteSpace(error.Error));
        Assert.Equal("Укажите название рецепта.", error.Error);
    }

    [Fact]
    public async Task Endpoints_WithoutToken_ReturnUnauthorized()
    {
        using var client = new ApiFactory().CreateClient();

        var list = await client.GetAsync("/api/recipes");
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);

        var create = await client.PostAsJsonAsync("/api/recipes", FullRequest());
        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
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

    private static async Task<HttpResponseMessage> DeleteAuthorizedAsync(
        HttpClient client, string token, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}