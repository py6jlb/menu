using MenuPlanner.Api.Data;
using MenuPlanner.Api.Recipes.Files;
using MenuPlanner.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using MenuPlanner.Domain.Accounts;
using MenuPlanner.Domain.Families;
using MenuPlanner.Domain.Recipes;

namespace MenuPlanner.Api.Tests.Recipes;

/// <summary>
/// Безопасная уборка файлов рецепта, параметризованная видом: удаляются только
/// управляемые имена, не упомянутые ни одним рецептом и старше безопасного
/// возраста. Актуальный файл, свежий незавершённый файл и посторонний объект в
/// каталоге не трогаются.
/// </summary>
public sealed class RecipeGarbageCollectorTests
{
    [Theory]
    [InlineData(RecipeFileKind.Photo)]
    [InlineData(RecipeFileKind.Document)]
    public async Task Collect_DeletesOnlyOldUnreferencedManagedFiles(RecipeFileKind kind)
    {
        var store = new InMemoryRecipeFileStore();
        var old = DateTime.UtcNow.AddDays(-2);
        var referenced = ManagedName(kind);
        var oldOrphan = ManagedName(kind);
        var freshOrphan = ManagedName(kind);
        store.Put(kind, referenced, lastWriteUtc: old);
        store.Put(kind, oldOrphan, lastWriteUtc: old);
        store.Put(kind, freshOrphan, lastWriteUtc: DateTime.UtcNow);
        store.Put(kind, "readme.txt", lastWriteUtc: old);

        await using var db = NewDb();
        await SeedRecipeAsync(db, kind, referenced);

        var collector = new RecipeGarbageCollector(
            db, store, TimeProvider.System, NullLogger<RecipeGarbageCollector>.Instance);

        var result = await collector.CollectAsync(kind, TimeSpan.FromHours(1));

        Assert.Equal(3, result.Scanned);
        Assert.Equal(1, result.Deleted);
        Assert.Equal(1, result.KeptReferenced);
        Assert.Equal(1, result.KeptFresh);
        Assert.Equal(0, result.Failed);
        Assert.True(store.Contains(kind, referenced));
        Assert.False(store.Contains(kind, oldOrphan));
        Assert.True(store.Contains(kind, freshOrphan));
        Assert.True(store.Contains(kind, "readme.txt"));
    }

    [Theory]
    [InlineData(RecipeFileKind.Photo)]
    [InlineData(RecipeFileKind.Document)]
    public async Task Collect_WhenDeleteFails_ReportsFailureAndKeepsFile(RecipeFileKind kind)
    {
        var store = new InMemoryRecipeFileStore();
        var orphan = ManagedName(kind);
        store.Put(kind, orphan, lastWriteUtc: DateTime.UtcNow.AddDays(-2));
        store.FailingDelete = (k, name) => k == kind && name == orphan;

        await using var db = NewDb();
        var collector = new RecipeGarbageCollector(
            db, store, TimeProvider.System, NullLogger<RecipeGarbageCollector>.Instance);

        var result = await collector.CollectAsync(kind, TimeSpan.FromHours(1));

        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.Deleted);
        Assert.True(store.Contains(kind, orphan));
    }

    private static string ManagedName(RecipeFileKind kind)
    {
        var extension = kind == RecipeFileKind.Photo ? ".png" : ".pdf";
        return $"{Guid.NewGuid():N}-{Guid.NewGuid():N}{extension}";
    }

    private static void SetPath(Recipe recipe, RecipeFileKind kind, string path)
    {
        if (kind == RecipeFileKind.Photo) recipe.PhotoPath = path;
        else recipe.DocumentPath = path;
    }

    private static async Task SeedRecipeAsync(AppDbContext db, RecipeFileKind kind, string path)
    {
        var owner = new User
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@example.com",
            PasswordHash = "hash",
            Role = UserRole.User,
            CreatedAt = DateTime.UtcNow
        };
        var family = new Family
        {
            Id = Guid.NewGuid(),
            Name = "Семья",
            InviteCode = Guid.NewGuid().ToString("N")[..8],
            OwnerId = owner.Id,
            CreatedAt = DateTime.UtcNow
        };
        var recipe = new Recipe
        {
            Id = Guid.NewGuid(),
            FamilyId = family.Id,
            Name = "Борщ",
            CookTimeMinutes = 10,
            Servings = 2,
            Difficulty = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        SetPath(recipe, kind, path);
        db.Users.Add(owner);
        db.Families.Add(family);
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();
    }

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
