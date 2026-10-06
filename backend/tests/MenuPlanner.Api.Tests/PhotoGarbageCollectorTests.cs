using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Recipes.Photos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MenuPlanner.Api.Tests;

/// <summary>
/// Безопасная уборка: удаляются только управляемые имена, не упомянутые ни одним
/// рецептом и старше безопасного возраста. Актуальное фото, свежий незавершённый
/// файл и посторонний объект в каталоге не трогаются.
/// </summary>
public sealed class PhotoGarbageCollectorTests
{
    [Fact]
    public async Task Collect_DeletesOnlyOldUnreferencedManagedFiles()
    {
        var store = new InMemoryPhotoStore();
        var old = DateTime.UtcNow.AddDays(-2);
        var referenced = ManagedName();
        var oldOrphan = ManagedName();
        var freshOrphan = ManagedName();
        store.Put(referenced, lastWriteUtc: old);
        store.Put(oldOrphan, lastWriteUtc: old);
        store.Put(freshOrphan, lastWriteUtc: DateTime.UtcNow);
        store.Put("readme.txt", lastWriteUtc: old);

        await using var db = NewDb();
        await SeedRecipeAsync(db, referenced);

        var collector = new PhotoGarbageCollector(
            db, store, TimeProvider.System, NullLogger<PhotoGarbageCollector>.Instance);

        var result = await collector.CollectAsync(TimeSpan.FromHours(1));

        Assert.Equal(3, result.Scanned);
        Assert.Equal(1, result.Deleted);
        Assert.Equal(1, result.KeptReferenced);
        Assert.Equal(1, result.KeptFresh);
        Assert.Equal(0, result.Failed);
        Assert.True(store.Contains(referenced));
        Assert.False(store.Contains(oldOrphan));
        Assert.True(store.Contains(freshOrphan));
        Assert.True(store.Contains("readme.txt"));
    }

    [Fact]
    public async Task Collect_WhenDeleteFails_ReportsFailureAndKeepsFile()
    {
        var store = new InMemoryPhotoStore();
        var orphan = ManagedName();
        store.Put(orphan, lastWriteUtc: DateTime.UtcNow.AddDays(-2));
        store.FailingDelete = name => name == orphan;

        await using var db = NewDb();
        var collector = new PhotoGarbageCollector(
            db, store, TimeProvider.System, NullLogger<PhotoGarbageCollector>.Instance);

        var result = await collector.CollectAsync(TimeSpan.FromHours(1));

        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.Deleted);
        Assert.True(store.Contains(orphan));
    }

    private static string ManagedName() =>
        $"{Guid.NewGuid():N}-{Guid.NewGuid():N}.png";

    private static async Task SeedRecipeAsync(AppDbContext db, string photoPath)
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
            PhotoPath = photoPath,
            CookTimeMinutes = 10,
            Servings = 2,
            Difficulty = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
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
