using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Recipes;
using MenuPlanner.Api.Recipes.Sharing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuPlanner.Api.Tests.Postgres;

/// <summary>
/// Гарантия импорта внешнего рецепта под настоящей PostgreSQL: параллельный импорт
/// одного источника в одну семью не создаёт дубль — уникальный индекс
/// <c>(FamilyId, SourceRecipeId)</c> пропускает одного, а проигравший перечитывает
/// победителя и возвращает его id. Пересечение задаётся барьером на самой вставке,
/// без sleep.
/// </summary>
public sealed class PostgresSharedRecipeImportTests : PostgresTestBase
{
    [PostgresFact]
    public async Task ConcurrentImport_KeepsSingleWrapper_AndLoserReturnsWinnerId()
    {
        var (sourceFamily, source, token) = await SeedSharedSourceAsync();
        var recipientFamilyId = await SeedFamilyAsync("Семья получателя");

        var barrier = new AsyncBarrier(2);

        async Task<SharedRecipeImportResult> ImportAsync()
        {
            await using var db = CreateContext(
                new BarrierNonQueryInterceptor(barrier, "INSERT INTO \"Recipes\""));
            return await new SharedRecipeImportService(db, TimeProvider.System)
                .ImportAsync(token, recipientFamilyId);
        }

        var results = await Task.WhenAll(ImportAsync(), ImportAsync());

        var created = Assert.Single(results, r => r.Outcome == SharedRecipeImportOutcome.Created);
        var already = Assert.Single(results, r => r.Outcome == SharedRecipeImportOutcome.AlreadyImported);
        Assert.Equal(created.RecipeId, already.RecipeId);

        await using var verify = Database.CreateContext();
        var wrappers = await verify.Recipes
            .Where(r => r.FamilyId == recipientFamilyId && r.SourceRecipeId == source.Id)
            .ToListAsync();
        var wrapper = Assert.Single(wrappers);
        Assert.Equal(created.RecipeId, wrapper.Id);
        Assert.Equal(sourceFamily.Id, wrapper.SourceFamilyId);
        Assert.Equal(token, wrapper.SourceToken);
    }

    private AppDbContext CreateContext(BarrierNonQueryInterceptor interceptor) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Database.ConnectionString)
            .AddInterceptors(interceptor)
            .Options);

    private async Task<(Family SourceFamily, Recipe Source, string Token)> SeedSharedSourceAsync()
    {
        var owner = PostgresData.NewUser($"import-{Guid.NewGuid():N}@example.com");
        var family = PostgresData.NewFamily("Семья источника", $"INV-{Guid.NewGuid().ToString("N")[..8]}", owner.Id);
        var recipe = PostgresData.NewRecipe(family.Id, "Борщ");
        const string token = "import-race-token";

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
        db.RecipeShares.Add(new RecipeShare
        {
            Id = Guid.NewGuid(),
            RecipeId = recipe.Id,
            Token = token,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        return (family, recipe, token);
    }

    private async Task<Guid> SeedFamilyAsync(string name)
    {
        var owner = PostgresData.NewUser($"recipient-{Guid.NewGuid():N}@example.com");
        var family = PostgresData.NewFamily(name, $"INV-{Guid.NewGuid().ToString("N")[..8]}", owner.Id);

        await using var db = Database.CreateContext();
        db.Users.Add(owner);
        db.Families.Add(family);
        db.FamilyMembers.Add(new FamilyMember
        {
            FamilyId = family.Id,
            UserId = owner.Id,
            JoinedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        return family.Id;
    }
}
