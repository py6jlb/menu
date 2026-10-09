using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Recipes;
using MenuPlanner.Api.Recipes.Documents;
using MenuPlanner.Api.Recipes.External;
using MenuPlanner.Api.Recipes.Photos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MenuPlanner.Api.Tests.Postgres;

/// <summary>
/// Жизненный цикл фото на настоящей PostgreSQL: конкурирующие замены оставляют
/// ровно файл победителя и без сирот, а уборка удаляет только действительно
/// бесхозные файлы, не теряя актуальное фото источника и скопированное фото.
/// </summary>
public sealed class PostgresPhotoLifecycleTests : PostgresTestBase
{
    [PostgresFact]
    public async Task ConcurrentReplacement_KeepsWinnerFile_AndNoOrphan()
    {
        var family = await SeedFamilyAsync();
        var recipe = await SeedRecipeAsync(family.Id, "Борщ");
        var (storage, root) = NewStorage();
        var barrier = new AsyncBarrier(2);

        async Task<RecipeMutationResult> UploadAsync(byte marker)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(Database.ConnectionString)
                .AddInterceptors(new BarrierNonQueryInterceptor(barrier, "UPDATE \"Recipes\""))
                .Options;
            await using var db = new AppDbContext(options);
            var service = new RecipeMutationService(
                db,
                NewLifecycle(storage),
                NewDocumentLifecycle(),
                TimeProvider.System,
                new RecipeRevisionReader(db));
            return await service.UploadPhotoAsync(
                new RecipeTarget(recipe.Id, family.Id),
                recipe.Revision,
                ".png",
                new MemoryStream(new[] { marker }));
        }

        var results = await Task.WhenAll(UploadAsync(1), UploadAsync(2));

        Assert.Single(results, r => r.Outcome == RecipeMutationOutcome.Ok);
        Assert.Single(results, r => r.Outcome == RecipeMutationOutcome.Conflict);

        var files = Directory.GetFiles(root, $"{recipe.Id:N}-*");
        Assert.Single(files);

        await using var verify = Database.CreateContext();
        var stored = await verify.Recipes.SingleAsync(r => r.Id == recipe.Id);
        Assert.Equal(2, stored.Revision);
        Assert.NotNull(stored.PhotoPath);
        Assert.True(File.Exists(Path.Combine(root, stored.PhotoPath!)));
        Assert.Equal(Path.GetFileName(files[0]), stored.PhotoPath);

        // Инспекция после уборки: актуальное фото победителя не потеряно.
        await using var collectorDb = Database.CreateContext();
        var collector = new PhotoGarbageCollector(
            collectorDb, storage, TimeProvider.System, NullLogger<PhotoGarbageCollector>.Instance);
        var cleanup = await collector.CollectAsync(TimeSpan.Zero);
        Assert.Equal(0, cleanup.Deleted);
        Assert.True(File.Exists(Path.Combine(root, stored.PhotoPath!)));
    }

    [PostgresFact]
    public async Task ConcurrentDeleteAndReplacement_LeavesConsistentFiles()
    {
        var family = await SeedFamilyAsync();
        var recipe = await SeedRecipeAsync(family.Id, "Борщ");
        var (storage, root) = NewStorage();
        var initialName = await storage.SaveAsync(
            recipe.Id, ".png", new MemoryStream(new byte[] { 0x01 }));
        await SetPhotoAsync(recipe.Id, initialName);

        int revision;
        await using (var db = Database.CreateContext())
        {
            revision = (await db.Recipes.SingleAsync(r => r.Id == recipe.Id)).Revision;
        }

        var barrier = new AsyncBarrier(2);
        var uploadOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Database.ConnectionString)
            .AddInterceptors(new BarrierNonQueryInterceptor(barrier, "UPDATE \"Recipes\""))
            .Options;
        var deleteOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Database.ConnectionString)
            .AddInterceptors(new BarrierNonQueryInterceptor(barrier, "DELETE FROM \"Recipes\""))
            .Options;

        async Task<RecipeMutationResult> UploadAsync()
        {
            await using var db = new AppDbContext(uploadOptions);
            var service = new RecipeMutationService(
                db, NewLifecycle(storage), NewDocumentLifecycle(), TimeProvider.System, new RecipeRevisionReader(db));
            return await service.UploadPhotoAsync(
                new RecipeTarget(recipe.Id, family.Id), revision, ".png", new MemoryStream(new byte[] { 0x02 }));
        }

        async Task<RecipeMutationResult> DeleteAsync()
        {
            await using var db = new AppDbContext(deleteOptions);
            var service = new RecipeMutationService(
                db, NewLifecycle(storage), NewDocumentLifecycle(), TimeProvider.System, new RecipeRevisionReader(db));
            return await service.DeleteAsync(new RecipeTarget(recipe.Id, family.Id), revision);
        }

        var results = await Task.WhenAll(UploadAsync(), DeleteAsync());
        Assert.Single(results, r => r.Outcome == RecipeMutationOutcome.Ok);

        await using var verify = Database.CreateContext();
        var stored = await verify.Recipes.SingleOrDefaultAsync(r => r.Id == recipe.Id);
        if (stored is not null)
        {
            // Победила замена: актуальное фото существует, файл не потерян.
            Assert.Equal(2, stored.Revision);
            Assert.NotNull(stored.PhotoPath);
            Assert.True(File.Exists(Path.Combine(root, stored.PhotoPath!)));
        }
        else
        {
            // Победило удаление: рецепта и его файла нет.
            Assert.False(File.Exists(Path.Combine(root, initialName)));
        }

        await using var collectorDb = Database.CreateContext();
        var collector = new PhotoGarbageCollector(
            collectorDb, storage, TimeProvider.System, NullLogger<PhotoGarbageCollector>.Instance);
        var cleanup = await collector.CollectAsync(TimeSpan.Zero);
        Assert.Equal(0, cleanup.Failed);
        if (stored is not null)
            Assert.True(File.Exists(Path.Combine(root, stored.PhotoPath!)));
    }

    [PostgresFact]
    public async Task Promotion_CopiesSourcePhoto_AndCleanupKeepsReferencedFiles()
    {
        var sourceFamily = await SeedFamilyAsync();
        var source = await SeedRecipeAsync(sourceFamily.Id, "Источник");
        var (storage, root) = NewStorage();
        var sourceName = await storage.SaveAsync(
            source.Id, ".png", new MemoryStream(new byte[] { 0x89, 0x50 }));
        await SetPhotoAsync(source.Id, sourceName);

        var recipientFamily = await SeedFamilyAsync();
        var wrapper = await SeedWrapperAsync(recipientFamily.Id, source, sourceFamily.Id);

        await using (var db = Database.CreateContext())
        {
            var promotion = new ExternalRecipePromotionService(
                db,
                new SourceFamilyNameResolver(db),
                NewLifecycle(storage),
                NewDocumentLifecycle(),
                TimeProvider.System,
                new RecipeRevisionReader(db));
            var result = await promotion.PromoteAsync(
                new RecipeTarget(wrapper.Id, recipientFamily.Id), wrapper.Revision);
            Assert.Equal(RecipePromotionOutcome.Promoted, result.Outcome);
        }

        await using var verify = Database.CreateContext();
        var storedWrapper = await verify.Recipes.SingleAsync(r => r.Id == wrapper.Id);
        Assert.NotNull(storedWrapper.PhotoPath);
        Assert.NotEqual(sourceName, storedWrapper.PhotoPath);
        Assert.True(File.Exists(Path.Combine(root, storedWrapper.PhotoPath!)));
        Assert.True(File.Exists(Path.Combine(root, sourceName)));

        // Посторонний старый файл уборка уносит, обе актуальные ссылки — нет.
        var orphanName = await storage.SaveAsync(
            Guid.NewGuid(), ".png", new MemoryStream(new byte[] { 0x01 }));
        var old = DateTime.UtcNow.AddDays(-2);
        File.SetLastWriteTimeUtc(Path.Combine(root, sourceName), old);
        File.SetLastWriteTimeUtc(Path.Combine(root, storedWrapper.PhotoPath!), old);
        File.SetLastWriteTimeUtc(Path.Combine(root, orphanName), old);

        await using var collectorDb = Database.CreateContext();
        var collector = new PhotoGarbageCollector(
            collectorDb, storage, TimeProvider.System, NullLogger<PhotoGarbageCollector>.Instance);
        var cleanup = await collector.CollectAsync(TimeSpan.FromHours(1));

        Assert.Equal(1, cleanup.Deleted);
        Assert.Equal(0, cleanup.Failed);
        Assert.False(File.Exists(Path.Combine(root, orphanName)));
        Assert.True(File.Exists(Path.Combine(root, sourceName)));
        Assert.True(File.Exists(Path.Combine(root, storedWrapper.PhotoPath!)));
    }

    private static PhotoLifecycle NewLifecycle(PhotoStorage store) =>
        new(store, NullLogger<PhotoLifecycle>.Instance);

    private static DocumentLifecycle NewDocumentLifecycle() =>
        new(new InMemoryDocumentStore(), NullLogger<DocumentLifecycle>.Instance);

    private static (PhotoStorage Storage, string Root) NewStorage()
    {
        var root = Path.Combine(
            Path.GetTempPath(), "menu_planner_photo_lifecycle_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PHOTOS_DIR"] = root })
            .Build();
        return (new PhotoStorage(configuration), root);
    }

    private async Task<Family> SeedFamilyAsync()
    {
        var owner = PostgresData.NewUser($"photo-{Guid.NewGuid():N}@example.com");
        var family = PostgresData.NewFamily("Семья", $"PH-{Guid.NewGuid().ToString("N")[..8]}", owner.Id);

        await using var db = Database.CreateContext();
        db.Users.Add(owner);
        db.Families.Add(family);
        await db.SaveChangesAsync();
        return family;
    }

    private async Task<Recipe> SeedRecipeAsync(Guid familyId, string name)
    {
        var recipe = PostgresData.NewRecipe(familyId, name);
        await using var db = Database.CreateContext();
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();
        return recipe;
    }

    private async Task<Recipe> SeedWrapperAsync(Guid familyId, Recipe source, Guid sourceFamilyId)
    {
        var wrapper = PostgresData.NewRecipe(familyId, source.Name);
        wrapper.SourceRecipeId = source.Id;
        wrapper.SourceFamilyId = sourceFamilyId;
        wrapper.SourceToken = "source-token";

        await using var db = Database.CreateContext();
        db.Recipes.Add(wrapper);
        await db.SaveChangesAsync();
        return wrapper;
    }

    private async Task SetPhotoAsync(Guid recipeId, string photoName)
    {
        await using var db = Database.CreateContext();
        var recipe = await db.Recipes.SingleAsync(r => r.Id == recipeId);
        recipe.PhotoPath = photoName;
        await db.SaveChangesAsync();
    }
}
