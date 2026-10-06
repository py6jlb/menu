using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Recipes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuPlanner.Api.Tests.Postgres;

/// <summary>
/// Гарантии ссылок на рецепт под настоящей PostgreSQL: уникальность одной ссылки
/// на рецепт и предсказуемое схождение параллельных create/revoke/regenerate.
/// Пересечение операций задаётся барьером на самой команде БД, без sleep.
/// </summary>
public sealed class PostgresRecipeShareTests : PostgresTestBase
{
    [PostgresFact]
    public async Task ConcurrentFirstCreate_KeepsSingleLink_AndLoserReturnsExisting()
    {
        var (owner, _, recipe) = await SeedFamilyWithMembershipAsync();
        var principal = PrincipalFor(owner.Id);
        var barrier = new AsyncBarrier(2);

        async Task<RecipeShareAccess> CreateAsync()
        {
            await using var db = NewContext(
                new BarrierNonQueryInterceptor(barrier, "INSERT INTO \"RecipeShares\""));
            return await NewService(db).CreateAsync(recipe.Id, principal);
        }

        var results = await Task.WhenAll(CreateAsync(), CreateAsync());

        Assert.All(results, r => Assert.Equal(RecipeShareOutcome.Ok, r.Outcome));
        Assert.Single(results, r => r.Created);
        Assert.Single(results.Select(r => r.Share!.Token).Distinct());

        await using var verify = Database.CreateContext();
        Assert.Equal(1, await verify.RecipeShares.CountAsync(s => s.RecipeId == recipe.Id));
    }

    [PostgresFact]
    public async Task ConcurrentRevoke_ConvergesOnRevokedState()
    {
        var (owner, _, recipe) = await SeedFamilyWithMembershipAsync();
        await SeedShareAsync(recipe.Id, "token-revoke");
        var principal = PrincipalFor(owner.Id);
        var barrier = new AsyncBarrier(2);

        async Task<RecipeShareAccess> RevokeAsync()
        {
            await using var db = NewContext(
                new BarrierNonQueryInterceptor(barrier, "UPDATE \"RecipeShares\""));
            return await NewService(db).RevokeAsync(recipe.Id, principal);
        }

        var results = await Task.WhenAll(RevokeAsync(), RevokeAsync());

        Assert.All(results, r => Assert.Equal(RecipeShareOutcome.Ok, r.Outcome));

        await using var verify = Database.CreateContext();
        var share = await verify.RecipeShares.SingleAsync(s => s.RecipeId == recipe.Id);
        Assert.NotNull(share.RevokedAt);
    }

    [PostgresFact]
    public async Task ConcurrentRegenerate_ConvergesOnSingleToken()
    {
        var (owner, _, recipe) = await SeedFamilyWithMembershipAsync();
        await SeedShareAsync(recipe.Id, "token-before");
        var principal = PrincipalFor(owner.Id);
        var barrier = new AsyncBarrier(2);

        async Task<RecipeShareAccess> RegenerateAsync()
        {
            await using var db = NewContext(
                new BarrierNonQueryInterceptor(barrier, "UPDATE \"RecipeShares\""));
            return await NewService(db).RegenerateAsync(recipe.Id, principal);
        }

        var results = await Task.WhenAll(RegenerateAsync(), RegenerateAsync());

        Assert.All(results, r => Assert.Equal(RecipeShareOutcome.Ok, r.Outcome));
        var token = Assert.Single(results.Select(r => r.Share!.Token).Distinct());
        Assert.NotEqual("token-before", token);

        await using var verify = Database.CreateContext();
        var share = await verify.RecipeShares.SingleAsync(s => s.RecipeId == recipe.Id);
        Assert.Equal(token, share.Token);
        Assert.Null(share.RevokedAt);
    }

    [PostgresFact]
    public async Task ConcurrentRevokeAndRegenerate_LeaveSingleConsistentLink()
    {
        var (owner, _, recipe) = await SeedFamilyWithMembershipAsync();
        await SeedShareAsync(recipe.Id, "token-both");
        var principal = PrincipalFor(owner.Id);
        var barrier = new AsyncBarrier(2);

        async Task<RecipeShareAccess> RevokeAsync()
        {
            await using var db = NewContext(
                new BarrierNonQueryInterceptor(barrier, "UPDATE \"RecipeShares\""));
            return await NewService(db).RevokeAsync(recipe.Id, principal);
        }

        async Task<RecipeShareAccess> RegenerateAsync()
        {
            await using var db = NewContext(
                new BarrierNonQueryInterceptor(barrier, "UPDATE \"RecipeShares\""));
            return await NewService(db).RegenerateAsync(recipe.Id, principal);
        }

        var results = await Task.WhenAll(RevokeAsync(), RegenerateAsync());

        Assert.All(results, r => Assert.Equal(RecipeShareOutcome.Ok, r.Outcome));

        await using var verify = Database.CreateContext();
        var share = await verify.RecipeShares.SingleAsync(s => s.RecipeId == recipe.Id);
        Assert.NotEqual("token-both", share.Token);
    }

    private AppDbContext NewContext(BarrierNonQueryInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Database.ConnectionString);
        if (interceptor is not null)
            builder.AddInterceptors(interceptor);
        return new AppDbContext(builder.Options);
    }

    private static RecipeSharingService NewService(AppDbContext db) =>
        new(db, new CurrentUserContext(db), TimeProvider.System);

    private static ClaimsPrincipal PrincipalFor(Guid userId) =>
        new(new ClaimsIdentity(
            new[] { new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()) }));

    private async Task<(User Owner, Family Family, Recipe Recipe)> SeedFamilyWithMembershipAsync()
    {
        var owner = PostgresData.NewUser($"share-{Guid.NewGuid():N}@example.com");
        var family = PostgresData.NewFamily("Семья", $"INV-{Guid.NewGuid().ToString("N")[..8]}", owner.Id);
        var recipe = PostgresData.NewRecipe(family.Id, "Борщ");

        await using var db = Database.CreateContext();
        db.Users.Add(owner);
        db.Families.Add(family);
        db.FamilyMembers.Add(new FamilyMember
        {
            FamilyId = family.Id,
            UserId = owner.Id,
            JoinedAt = DateTime.UtcNow
        });
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();

        return (owner, family, recipe);
    }

    private async Task SeedShareAsync(Guid recipeId, string token)
    {
        await using var db = Database.CreateContext();
        db.RecipeShares.Add(new RecipeShare
        {
            Id = Guid.NewGuid(),
            RecipeId = recipeId,
            Token = token,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }
}
