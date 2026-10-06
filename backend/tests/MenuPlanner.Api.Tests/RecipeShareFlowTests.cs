using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Families;
using MenuPlanner.Api.Recipes;

namespace MenuPlanner.Api.Tests;

public sealed class RecipeShareFlowTests
{
    [Fact]
    public async Task Create_FirstCallCreatesLink_SecondReturnsSameWithoutDuplicating()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var recipe = await CreateRecipeAsync(client, owner.Token, "Борщ");

        var (firstResponse, first) = await PostAuthorizedAsync<RecipeShareDto>(
            client, owner.Token, $"/api/recipes/{recipe.Id}/share", new { });
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.NotNull(first);
        Assert.False(first.Revoked);
        Assert.False(string.IsNullOrWhiteSpace(first.Token));
        Assert.Equal($"/r/{first.Token}", first.Path);
        Assert.Equal($"https://menu.example.com/r/{first.Token}", first.Url);

        var (secondResponse, second) = await PostAuthorizedAsync<RecipeShareDto>(
            client, owner.Token, $"/api/recipes/{recipe.Id}/share", new { });
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(first.Token, second!.Token);
        Assert.Equal(first.Url, second.Url);

        Assert.Equal(1, await CountSharesAsync(factory, recipe.Id));
    }

    [Fact]
    public async Task Read_WithoutShare_ReturnsNotFound_AndCreatesNothing()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var recipe = await CreateRecipeAsync(client, owner.Token, "Борщ");

        var (response, error) = await GetAuthorizedAsync<RecipeErrorDto>(
            client, owner.Token, $"/api/recipes/{recipe.Id}/share");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Ссылка ещё не создана.", error!.Error);
        Assert.Equal(0, await CountSharesAsync(factory, recipe.Id));
    }

    [Fact]
    public async Task Read_ExistingShare_ReturnsSameLinkWithoutRecreating()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var recipe = await CreateRecipeAsync(client, owner.Token, "Борщ");
        var (_, created) = await CreateShareAsync(client, owner.Token, recipe.Id);

        var (response, read) = await GetAuthorizedAsync<RecipeShareDto>(
            client, owner.Token, $"/api/recipes/{recipe.Id}/share");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(created!.Token, read!.Token);
        Assert.Equal(1, await CountSharesAsync(factory, recipe.Id));
    }

    [Fact]
    public async Task Create_ByFamilyMember_Succeeds()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        var member = await RegisterAsync(client, "member");
        var family = await CreateFamilyAsync(client, owner.Token, "Семья");
        await JoinFamilyAsync(client, member.Token, family.InviteCode);
        var recipe = await CreateRecipeAsync(client, owner.Token, "Борщ");

        var (response, share) = await CreateShareAsync(client, member.Token, recipe.Id);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(share);
        Assert.False(string.IsNullOrWhiteSpace(share.Token));
    }

    [Fact]
    public async Task Create_FromAnotherFamily_ReturnsNotFound()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var first = await RegisterAsync(client, "first");
        var second = await RegisterAsync(client, "second");
        await CreateFamilyAsync(client, first.Token, "Первая");
        await CreateFamilyAsync(client, second.Token, "Вторая");
        var recipe = await CreateRecipeAsync(client, first.Token, "Борщ");

        var (response, _) = await PostAuthorizedAsync<RecipeErrorDto>(
            client, second.Token, $"/api/recipes/{recipe.Id}/share", new { });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutFamily_ReturnsNotFound()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var lonely = await RegisterAsync(client, "lonely");
        var recipeId = Guid.NewGuid();

        var (response, _) = await PostAuthorizedAsync<RecipeErrorDto>(
            client, lonely.Token, $"/api/recipes/{recipeId}/share", new { });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WithoutToken_ReturnsUnauthorized()
    {
        using var client = new ApiFactory().CreateClient();
        var recipeId = Guid.NewGuid();

        var get = await client.GetAsync($"/api/recipes/{recipeId}/share");
        var create = await client.PostAsJsonAsync($"/api/recipes/{recipeId}/share", new { });
        var revoke = await client.DeleteAsync($"/api/recipes/{recipeId}/share");
        var regenerate = await client.PostAsJsonAsync($"/api/recipes/{recipeId}/share/regenerate", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, regenerate.StatusCode);
    }

    [Fact]
    public async Task Revoke_ByOwner_MarksLinkDead()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var recipe = await CreateRecipeAsync(client, owner.Token, "Борщ");
        var (_, share) = await CreateShareAsync(client, owner.Token, recipe.Id);

        var (response, revoked) = await DeleteAuthorizedAsync<RecipeShareDto>(
            client, owner.Token, $"/api/recipes/{recipe.Id}/share");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(revoked);
        Assert.True(revoked.Revoked);
        Assert.NotNull(revoked.RevokedAt);
        Assert.Equal(share!.Token, revoked.Token);

        var (afterResponse, after) = await GetAuthorizedAsync<RecipeShareDto>(
            client, owner.Token, $"/api/recipes/{recipe.Id}/share");
        Assert.Equal(HttpStatusCode.OK, afterResponse.StatusCode);
        Assert.True(after!.Revoked);
        Assert.Equal(share.Token, after.Token);
    }

    [Fact]
    public async Task Revoke_IsIdempotent()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var recipe = await CreateRecipeAsync(client, owner.Token, "Борщ");
        await CreateShareAsync(client, owner.Token, recipe.Id);

        var (firstResponse, first) = await DeleteAuthorizedAsync<RecipeShareDto>(
            client, owner.Token, $"/api/recipes/{recipe.Id}/share");
        var (secondResponse, second) = await DeleteAuthorizedAsync<RecipeShareDto>(
            client, owner.Token, $"/api/recipes/{recipe.Id}/share");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.True(first!.Revoked);
        Assert.True(second!.Revoked);
        Assert.Equal(first.RevokedAt, second.RevokedAt);
        Assert.Equal(1, await CountSharesAsync(factory, recipe.Id));
    }

    [Fact]
    public async Task Revoke_WithoutShare_ReturnsNotFound()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var recipe = await CreateRecipeAsync(client, owner.Token, "Борщ");

        var response = await DeleteAuthorizedAsync(
            client, owner.Token, $"/api/recipes/{recipe.Id}/share");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Member_CannotRevokeOrRegenerate_ReturnsForbidden()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        var member = await RegisterAsync(client, "member");
        var family = await CreateFamilyAsync(client, owner.Token, "Семья");
        await JoinFamilyAsync(client, member.Token, family.InviteCode);
        var recipe = await CreateRecipeAsync(client, owner.Token, "Борщ");
        await CreateShareAsync(client, owner.Token, recipe.Id);

        var revoke = await DeleteAuthorizedAsync(
            client, member.Token, $"/api/recipes/{recipe.Id}/share");
        var regenerate = await PostAuthorizedAsync<RecipeShareDto>(
            client, member.Token, $"/api/recipes/{recipe.Id}/share/regenerate", new { });

        Assert.Equal(HttpStatusCode.Forbidden, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, regenerate.Response.StatusCode);
    }

    [Fact]
    public async Task Regenerate_ByOwner_ReplacesToken()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var recipe = await CreateRecipeAsync(client, owner.Token, "Борщ");
        var (_, original) = await CreateShareAsync(client, owner.Token, recipe.Id);

        var revoke = await DeleteAuthorizedAsync(
            client, owner.Token, $"/api/recipes/{recipe.Id}/share");
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);

        var (response, regenerated) = await PostAuthorizedAsync<RecipeShareDto>(
            client, owner.Token, $"/api/recipes/{recipe.Id}/share/regenerate", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(regenerated);
        Assert.NotEqual(original!.Token, regenerated.Token);
        Assert.False(regenerated.Revoked);
        Assert.Null(regenerated.RevokedAt);
        Assert.Equal($"/r/{regenerated.Token}", regenerated.Path);

        Assert.Equal(1, await CountSharesAsync(factory, recipe.Id));
        Assert.False(await ShareTokenExistsAsync(factory, original.Token));
        Assert.True(await ShareTokenExistsAsync(factory, regenerated.Token));
    }

    [Fact]
    public async Task Regenerate_Repeatedly_KeepsSingleLiveLink()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var recipe = await CreateRecipeAsync(client, owner.Token, "Борщ");
        var (_, first) = await CreateShareAsync(client, owner.Token, recipe.Id);

        var (secondResponse, second) = await PostAuthorizedAsync<RecipeShareDto>(
            client, owner.Token, $"/api/recipes/{recipe.Id}/share/regenerate", new { });
        var (thirdResponse, third) = await PostAuthorizedAsync<RecipeShareDto>(
            client, owner.Token, $"/api/recipes/{recipe.Id}/share/regenerate", new { });

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, thirdResponse.StatusCode);
        Assert.NotEqual(first!.Token, second!.Token);
        Assert.NotEqual(second.Token, third!.Token);
        Assert.Equal(1, await CountSharesAsync(factory, recipe.Id));
    }

    [Fact]
    public async Task Regenerate_WithoutShare_ReturnsNotFound()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var recipe = await CreateRecipeAsync(client, owner.Token, "Борщ");

        var (response, _) = await PostAuthorizedAsync<RecipeErrorDto>(
            client, owner.Token, $"/api/recipes/{recipe.Id}/share/regenerate", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await CountSharesAsync(factory, recipe.Id));
    }

    [Fact]
    public async Task Create_OnExternalRecipe_ReturnsForbidden_AndDoesNotShareTransitively()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья источника");
        var source = await CreateRecipeAsync(client, owner.Token, "Борщ");
        var (_, share) = await CreateShareAsync(client, owner.Token, source.Id);

        var recipient = await RegisterAsync(client, "recipient");
        await CreateFamilyAsync(client, recipient.Token, "Семья получателя");
        var (importResponse, imported) = await PostAuthorizedAsync<RecipeImportResultDto>(
            client, recipient.Token, $"/api/shared/{share!.Token}/import", body: null);
        Assert.Equal(HttpStatusCode.Created, importResponse.StatusCode);

        var (createResponse, error) = await PostAuthorizedAsync<RecipeErrorDto>(
            client, recipient.Token, $"/api/recipes/{imported!.RecipeId}/share", new { });
        var (readResponse, _) = await GetAuthorizedAsync<RecipeErrorDto>(
            client, recipient.Token, $"/api/recipes/{imported.RecipeId}/share");

        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);
        Assert.Equal("Внешний рецепт доступен только для чтения.", error!.Error);
        Assert.Equal(HttpStatusCode.Forbidden, readResponse.StatusCode);
        Assert.Equal(0, await CountSharesAsync(factory, imported.RecipeId));
    }

    [Fact]
    public async Task Unverified_CannotCreateRevokeOrRegenerate_ReturnsForbidden()
    {
        using var factory = new UnverifiedApiFactory();
        using var client = factory.CreateClient();
        var unverified = await RegisterAsync(client, "unverified");
        var recipeId = Guid.NewGuid();

        var create = await PostAuthorizedAsync<ErrorDto>(
            client, unverified.Token, $"/api/recipes/{recipeId}/share", new { });
        var revoke = await DeleteAuthorizedAsync(
            client, unverified.Token, $"/api/recipes/{recipeId}/share");
        var regenerate = await PostAuthorizedAsync<ErrorDto>(
            client, unverified.Token, $"/api/recipes/{recipeId}/share/regenerate", new { });

        Assert.Equal(HttpStatusCode.Forbidden, create.Response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, regenerate.Response.StatusCode);
    }

    [Fact]
    public async Task Create_RaceOnUniqueIndex_ReturnsExistingLinkInsteadOfError()
    {
        using var factory = new ApiFactory();
        var race = new ShareRaceInjectingInterceptor(factory.DatabaseName, factory.DatabaseRoot);
        factory.Interceptors.Add(race);
        using var client = factory.CreateClient();

        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var recipe = await CreateRecipeAsync(client, owner.Token, "Борщ");

        // Гонка: «параллельный» запрос успевает создать ссылку до нашего SaveChanges.
        var concurrentId = Guid.NewGuid();
        var concurrentToken = Guid.NewGuid().ToString("N");
        race.Arm(recipe.Id, concurrentId, concurrentToken);

        var (response, share) = await CreateShareAsync(client, owner.Token, recipe.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(concurrentToken, share!.Token);
        Assert.Equal(1, await CountSharesAsync(factory, recipe.Id));
    }

    private static async Task<int> CountSharesAsync(ApiFactory factory, Guid recipeId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.RecipeShares.CountAsync(s => s.RecipeId == recipeId);
    }

    private static async Task<bool> ShareTokenExistsAsync(ApiFactory factory, string token)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.RecipeShares.AnyAsync(s => s.Token == token);
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

    private static async Task<FamilyDto> CreateFamilyAsync(HttpClient client, string token, string name)
    {
        var (response, family) = await PostAuthorizedAsync<FamilyDto>(
            client, token, "/api/families", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return family!;
    }

    private static async Task JoinFamilyAsync(HttpClient client, string token, string inviteCode)
    {
        var (response, _) = await PostAuthorizedAsync<FamilyDto>(
            client, token, "/api/families/join", new { inviteCode });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<RecipeDto> CreateRecipeAsync(HttpClient client, string token, string name)
    {
        var body = new RecipeRequest(
            Name: name,
            Description: null,
            CookTimeMinutes: 30,
            Servings: 2,
            Difficulty: 1,
            Calories: null,
            Tags: new List<string>(),
            Seasonality: new List<string>(),
            Diet: new List<string>(),
            Steps: new List<RecipeStepRequest> { new("Смешать.") },
            Ingredients: new List<RecipeIngredientRequest> { new("Соль", 1, "tsp", null) });

        var (response, recipe) = await PostAuthorizedAsync<RecipeDto>(client, token, "/api/recipes", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return recipe!;
    }

    private static Task<(HttpResponseMessage Response, RecipeShareDto? Data)> CreateShareAsync(
        HttpClient client, string token, Guid recipeId) =>
        PostAuthorizedAsync<RecipeShareDto>(client, token, $"/api/recipes/{recipeId}/share", new { });

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

    private static async Task<(HttpResponseMessage Response, T? Data)> GetAuthorizedAsync<T>(
        HttpClient client, string token, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        var data = await ReadJsonAsync<T>(response);
        return (response, data);
    }

    private static async Task<(HttpResponseMessage Response, T? Data)> DeleteAuthorizedAsync<T>(
        HttpClient client, string token, string path)
    {
        var response = await DeleteAuthorizedAsync(client, token, path);
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

    private sealed class UnverifiedApiFactory : ApiFactory
    {
        public UnverifiedApiFactory() => AutoVerifyEmailsOnRegistration = false;
    }

    /// <summary>
    /// Симулирует гонку первого создания: при вставке ссылки сначала создаёт
    /// «параллельную» ссылку в той же in-memory базе, а затем бросает
    /// <see cref="DbUpdateException"/>, как это сделал бы уникальный индекс <c>RecipeId</c>.
    /// </summary>
    private sealed class ShareRaceInjectingInterceptor : SaveChangesInterceptor
    {
        private readonly string _databaseName;
        private readonly InMemoryDatabaseRoot _databaseRoot;
        private bool _armed;
        private bool _fired;
        private Guid _recipeId;
        private Guid _duplicateId;
        private string _duplicateToken = "";

        public ShareRaceInjectingInterceptor(string databaseName, InMemoryDatabaseRoot databaseRoot)
        {
            _databaseName = databaseName;
            _databaseRoot = databaseRoot;
        }

        public void Arm(Guid recipeId, Guid duplicateId, string duplicateToken)
        {
            _recipeId = recipeId;
            _duplicateId = duplicateId;
            _duplicateToken = duplicateToken;
            _armed = true;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (_armed && !_fired && eventData.Context is AppDbContext context)
            {
                var isShareInsert = context.ChangeTracker.Entries<RecipeShare>()
                    .Any(e => e.State == EntityState.Added && e.Entity.RecipeId == _recipeId);
                if (isShareInsert)
                {
                    _fired = true;
                    await InsertDuplicateAsync(cancellationToken);
                    throw new DbUpdateException(
                        "Симуляция нарушения уникального индекса RecipeId.");
                }
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private async Task InsertDuplicateAsync(CancellationToken cancellationToken)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(_databaseName, _databaseRoot)
                .Options;
            using var db = new AppDbContext(options);
            db.RecipeShares.Add(new RecipeShare
            {
                Id = _duplicateId,
                RecipeId = _recipeId,
                Token = _duplicateToken,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
