using System.Text;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Recipes;
using MenuPlanner.Api.Recipes.External;
using MenuPlanner.Api.Recipes.Photos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MenuPlanner.Api.Tests;

/// <summary>
/// Жизненный цикл фото у сервисов правки и промоушена: неуспех записи БД убирает
/// новый файл, а ошибка удаления старого файла после commit не превращает уже
/// завершённое изменение в ложный общий сбой (файл остаётся кандидатом уборки).
/// </summary>
public sealed class PhotoLifecycleServiceTests
{
    private const string OldPhoto =
        "0123456789abcdef0123456789abcdef-0123456789abcdef0123456789abcdef.png";

    [Fact]
    public async Task UploadPhoto_WhenDbWriteFails_DiscardsStagedFile()
    {
        var store = new InMemoryPhotoStore();
        const string dbName = "photo-upload-db-fail";
        Guid recipeId;
        await using (var seed = NewDb(dbName))
        {
            var (seeded, _) = await SeedRecipeAsync(seed);
            recipeId = seeded.Id;
        }

        await using var db = NewDb(dbName, new ThrowOnRecipePhotoUpdateInterceptor());
        var recipe = await db.Recipes.SingleAsync(r => r.Id == recipeId);
        var service = NewService(db, store);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UploadPhotoAsync(
                new RecipeTarget(recipe.Id, recipe.FamilyId),
                recipe.Revision,
                ".png",
                new MemoryStream(new byte[] { 1, 2, 3 })));

        // Новый файл убран компенсацией, БД осталась прежней.
        Assert.Empty(store.Names);
        await using var verify = NewDb(dbName);
        var stored = await verify.Recipes.SingleAsync(r => r.Id == recipeId);
        Assert.Null(stored.PhotoPath);
        Assert.Equal(1, stored.Revision);
    }

    [Fact]
    public async Task UploadPhoto_WhenPreviousDeleteFails_StillCompletes_AndKeepsOldAsCandidate()
    {
        var store = new InMemoryPhotoStore();
        store.Put(OldPhoto, Encoding.ASCII.GetBytes("old"));
        store.FailingDelete = name => name == OldPhoto;

        await using var db = NewDb("photo-upload-retire");
        var (recipe, family) = await SeedRecipeAsync(db, photoPath: OldPhoto);
        var service = NewService(db, store);

        var result = await service.UploadPhotoAsync(
            new RecipeTarget(recipe.Id, family.Id),
            recipe.Revision,
            ".png",
            new MemoryStream(Encoding.ASCII.GetBytes("new")));

        Assert.Equal(RecipeMutationOutcome.Ok, result.Outcome);
        // Завершённое изменение не стало неуспешным; старый файл остался уборке.
        Assert.True(store.Contains(OldPhoto));
        Assert.Equal(2, store.Names.Count);
        var stored = await db.Recipes.SingleAsync(r => r.Id == recipe.Id);
        Assert.Equal(2, stored.Revision);
        Assert.Equal(result.Recipe!.PhotoPath, stored.PhotoPath);
        Assert.True(store.Contains(stored.PhotoPath!));
    }

    [Fact]
    public async Task DeletePhoto_WhenFileDeleteFails_StillCompletes()
    {
        var store = new InMemoryPhotoStore();
        store.Put(OldPhoto, Encoding.ASCII.GetBytes("old"));
        store.FailingDelete = name => name == OldPhoto;

        await using var db = NewDb("photo-delete-retire");
        var (recipe, family) = await SeedRecipeAsync(db, photoPath: OldPhoto);
        var service = NewService(db, store);

        var result = await service.DeletePhotoAsync(new RecipeTarget(recipe.Id, family.Id), recipe.Revision);

        Assert.Equal(RecipeMutationOutcome.Ok, result.Outcome);
        Assert.True(store.Contains(OldPhoto));
        var stored = await db.Recipes.SingleAsync(r => r.Id == recipe.Id);
        Assert.Null(stored.PhotoPath);
        Assert.Equal(2, stored.Revision);
    }

    [Fact]
    public async Task Promotion_WhenDbWriteFails_DiscardsCopiedFile()
    {
        var store = new InMemoryPhotoStore();
        store.Put(OldPhoto, Encoding.ASCII.GetBytes("source"));

        const string dbName = "photo-promotion-fail";
        Guid wrapperId;
        Guid recipientFamilyId;
        await using (var seed = NewDb(dbName))
        {
            var sourceFamily = NewFamily();
            var source = NewRecipe(sourceFamily.Id, "Источник");
            source.PhotoPath = OldPhoto;
            var recipientFamily = NewFamily();
            var wrapper = NewRecipe(recipientFamily.Id, "Внешний");
            wrapper.SourceRecipeId = source.Id;
            wrapper.SourceFamilyId = sourceFamily.Id;
            wrapper.SourceToken = "tok";
            seed.Families.AddRange(sourceFamily, recipientFamily);
            seed.Recipes.AddRange(source, wrapper);
            await seed.SaveChangesAsync();
            wrapperId = wrapper.Id;
            recipientFamilyId = recipientFamily.Id;
        }

        await using var db = NewDb(dbName, new ThrowOnRecipePhotoUpdateInterceptor());
        var promotion = new ExternalRecipePromotionService(
            db,
            new SourceFamilyNameResolver(db),
            new PhotoLifecycle(store, NullLogger<PhotoLifecycle>.Instance),
            TimeProvider.System,
            new RecipeRevisionReader(db));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            promotion.PromoteAsync(new RecipeTarget(wrapperId, recipientFamilyId), 1));

        // Скопированный файл убран компенсацией, файл источника не тронут.
        Assert.Equal(new[] { OldPhoto }, store.Names);
    }

    [Fact]
    public async Task ReplaceAsync_WhenCommitConfirmed_RetiresPrevious_AndKeepsNew()
    {
        var store = new InMemoryPhotoStore();
        store.Put(OldPhoto, Encoding.ASCII.GetBytes("old"));
        var lifecycle = new PhotoLifecycle(store, NullLogger<PhotoLifecycle>.Instance);

        var created = await lifecycle.ReplaceAsync(
            Guid.NewGuid(), ".png", new MemoryStream(new byte[] { 7 }), OldPhoto,
            staged => Task.FromResult(new PhotoCommit<string>(true, staged)));

        Assert.False(store.Contains(OldPhoto));
        Assert.Single(store.Names);
        Assert.True(store.Contains(created));
    }

    [Fact]
    public async Task ReplaceAsync_WhenNotCommitted_DiscardsNewFile_AndKeepsPrevious()
    {
        var store = new InMemoryPhotoStore();
        store.Put(OldPhoto, Encoding.ASCII.GetBytes("old"));
        var lifecycle = new PhotoLifecycle(store, NullLogger<PhotoLifecycle>.Instance);

        await lifecycle.ReplaceAsync(
            Guid.NewGuid(), ".png", new MemoryStream(new byte[] { 7 }), OldPhoto,
            staged => Task.FromResult(new PhotoCommit<string>(false, staged)));

        Assert.True(store.Contains(OldPhoto));
        Assert.Single(store.Names);
    }

    [Fact]
    public void Discard_WhenDeleteFails_DoesNotThrow_AndKeepsFileAsCandidate()
    {
        var store = new InMemoryPhotoStore();
        store.Put(OldPhoto, Encoding.ASCII.GetBytes("old"));
        store.FailingDelete = name => name == OldPhoto;
        var lifecycle = new PhotoLifecycle(store, NullLogger<PhotoLifecycle>.Instance);

        // Сбой компенсации не маскирует исходную ошибку и оставляет файл уборке.
        lifecycle.Discard(OldPhoto);

        Assert.True(store.Contains(OldPhoto));
    }

    private static RecipeMutationService NewService(AppDbContext db, IPhotoStore store) =>
        new(db,
            new PhotoLifecycle(store, NullLogger<PhotoLifecycle>.Instance),
            TimeProvider.System,
            new RecipeRevisionReader(db));

    private static AppDbContext NewDb(string name, params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name);
        if (interceptors.Length > 0) builder.AddInterceptors(interceptors);
        return new AppDbContext(builder.Options);
    }

    private static async Task<(Recipe Recipe, Family Family)> SeedRecipeAsync(
        AppDbContext db, string? photoPath = null)
    {
        var owner = new User
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@example.com",
            PasswordHash = "hash",
            Role = UserRole.User,
            CreatedAt = DateTime.UtcNow
        };
        var family = NewFamily(owner.Id);
        var recipe = NewRecipe(family.Id, "Борщ");
        recipe.PhotoPath = photoPath;
        db.Users.Add(owner);
        db.Families.Add(family);
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();
        return (recipe, family);
    }

    private static Family NewFamily(Guid? ownerId = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Семья",
        InviteCode = Guid.NewGuid().ToString("N")[..8],
        OwnerId = ownerId ?? Guid.NewGuid(),
        CreatedAt = DateTime.UtcNow
    };

    private static Recipe NewRecipe(Guid familyId, string name) => new()
    {
        Id = Guid.NewGuid(),
        FamilyId = familyId,
        Name = name,
        CookTimeMinutes = 10,
        Servings = 2,
        Difficulty = 1,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    /// <summary>Роняет запись правки фото рецепта, имитируя отказ БД после записи файла.</summary>
    private sealed class ThrowOnRecipePhotoUpdateInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is { } context
                && context.ChangeTracker.Entries<Recipe>().Any(entry =>
                    entry.State == EntityState.Modified
                    && entry.Property(r => r.PhotoPath).IsModified))
            {
                throw new InvalidOperationException("Симуляция отказа БД после записи файла.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
