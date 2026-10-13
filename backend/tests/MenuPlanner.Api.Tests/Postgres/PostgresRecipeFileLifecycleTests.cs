using MenuPlanner.Api.Data;
using MenuPlanner.Api.Recipes;
using MenuPlanner.Api.Recipes.Crud;
using MenuPlanner.Api.Recipes.External;
using MenuPlanner.Api.Recipes.Files;
using MenuPlanner.Api.Recipes.Reading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using MenuPlanner.Api.Domain.Families;
using MenuPlanner.Api.Domain.Recipes;

namespace MenuPlanner.Api.Tests.Postgres;

/// <summary>
/// Жизненный цикл файла рецепта (фото и документ) на настоящей PostgreSQL:
/// конкурирующие замены оставляют ровно файл победителя и без сирот, а уборка
/// удаляет только действительно бесхозные файлы, не теряя актуальный файл
/// источника и скопированный файл. Поведение одинаково для обоих видов.
/// </summary>
public sealed class PostgresRecipeFileLifecycleTests : PostgresTestBase
{
    [PostgresTheory]
    [InlineData(RecipeFileKind.Photo)]
    [InlineData(RecipeFileKind.Document)]
    public async Task ConcurrentReplacement_KeepsWinnerFile_AndNoOrphan(RecipeFileKind kind)
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
                db, NewLifecycle(storage), TimeProvider.System, new RecipeRevisionReader(db));
            return await UploadFileAsync(service, recipe, family, kind, marker);
        }

        var results = await Task.WhenAll(UploadAsync(1), UploadAsync(2));

        Assert.Single(results, r => r.Outcome == RecipeMutationOutcome.Ok);
        Assert.Single(results, r => r.Outcome == RecipeMutationOutcome.Conflict);

        var kindRoot = KindRoot(root, kind);
        var files = Directory.GetFiles(kindRoot, $"{recipe.Id:N}-*");
        Assert.Single(files);

        await using var verify = Database.CreateContext();
        var stored = await verify.Recipes.SingleAsync(r => r.Id == recipe.Id);
        Assert.Equal(2, stored.Revision);
        Assert.NotNull(GetPath(stored, kind));
        Assert.True(File.Exists(Path.Combine(kindRoot, GetPath(stored, kind)!)));
        Assert.Equal(Path.GetFileName(files[0]), GetPath(stored, kind));

        // Инспекция после уборки: актуальный файл победителя не потерян.
        await using var collectorDb = Database.CreateContext();
        var collector = new RecipeGarbageCollector(
            collectorDb, storage, TimeProvider.System, NullLogger<RecipeGarbageCollector>.Instance);
        var cleanup = await collector.CollectAsync(kind, TimeSpan.Zero);
        Assert.Equal(0, cleanup.Deleted);
        Assert.True(File.Exists(Path.Combine(kindRoot, GetPath(stored, kind)!)));
    }

    [PostgresTheory]
    [InlineData(RecipeFileKind.Photo)]
    [InlineData(RecipeFileKind.Document)]
    public async Task ConcurrentDeleteAndReplacement_LeavesConsistentFiles(RecipeFileKind kind)
    {
        var family = await SeedFamilyAsync();
        var recipe = await SeedRecipeAsync(family.Id, "Борщ");
        var (storage, root) = NewStorage();
        var initialName = await storage.SaveAsync(
            kind, recipe.Id, Extension(kind), new MemoryStream(new byte[] { 0x01 }));
        await SetPathAsync(kind, recipe.Id, initialName);

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
                db, NewLifecycle(storage), TimeProvider.System, new RecipeRevisionReader(db));
            return await UploadFileAsync(service, recipe, family, kind, 0x02);
        }

        async Task<RecipeMutationResult> DeleteAsync()
        {
            await using var db = new AppDbContext(deleteOptions);
            var service = new RecipeMutationService(
                db, NewLifecycle(storage), TimeProvider.System, new RecipeRevisionReader(db));
            return await service.DeleteAsync(new RecipeTarget(recipe.Id, family.Id), revision);
        }

        var results = await Task.WhenAll(UploadAsync(), DeleteAsync());
        Assert.Single(results, r => r.Outcome == RecipeMutationOutcome.Ok);

        var kindRoot = KindRoot(root, kind);
        await using var verify = Database.CreateContext();
        var stored = await verify.Recipes.SingleOrDefaultAsync(r => r.Id == recipe.Id);
        if (stored is not null)
        {
            // Победила замена: актуальный файл существует, файл не потерян.
            Assert.Equal(2, stored.Revision);
            Assert.NotNull(GetPath(stored, kind));
            Assert.True(File.Exists(Path.Combine(kindRoot, GetPath(stored, kind)!)));
        }
        else
        {
            // Победило удаление: рецепта и его файла нет.
            Assert.False(File.Exists(Path.Combine(kindRoot, initialName)));
        }

        await using var collectorDb = Database.CreateContext();
        var collector = new RecipeGarbageCollector(
            collectorDb, storage, TimeProvider.System, NullLogger<RecipeGarbageCollector>.Instance);
        var cleanup = await collector.CollectAsync(kind, TimeSpan.Zero);
        Assert.Equal(0, cleanup.Failed);
        if (stored is not null)
            Assert.True(File.Exists(Path.Combine(kindRoot, GetPath(stored, kind)!)));
    }

    [PostgresTheory]
    [InlineData(RecipeFileKind.Photo)]
    [InlineData(RecipeFileKind.Document)]
    public async Task Promotion_CopiesSourceFile_AndCleanupKeepsReferencedFiles(RecipeFileKind kind)
    {
        var sourceFamily = await SeedFamilyAsync();
        var source = await SeedRecipeAsync(sourceFamily.Id, "Источник");
        var (storage, root) = NewStorage();
        var sourceName = await storage.SaveAsync(
            kind, source.Id, Extension(kind), new MemoryStream(new byte[] { 0x89, 0x50 }));
        await SetPathAsync(kind, source.Id, sourceName);

        var recipientFamily = await SeedFamilyAsync();
        var wrapper = await SeedWrapperAsync(recipientFamily.Id, source, sourceFamily.Id);

        await using (var db = Database.CreateContext())
        {
            var promotion = new ExternalRecipePromotionService(
                db,
                new SourceFamilyNameResolver(db),
                NewLifecycle(storage),
                TimeProvider.System,
                new RecipeRevisionReader(db));
            var result = await promotion.PromoteAsync(
                new RecipeTarget(wrapper.Id, recipientFamily.Id), wrapper.Revision);
            Assert.Equal(RecipePromotionOutcome.Promoted, result.Outcome);
        }

        var kindRoot = KindRoot(root, kind);
        await using var verify = Database.CreateContext();
        var storedWrapper = await verify.Recipes.SingleAsync(r => r.Id == wrapper.Id);
        var copiedName = GetPath(storedWrapper, kind);
        Assert.NotNull(copiedName);
        Assert.NotEqual(sourceName, copiedName);
        Assert.True(File.Exists(Path.Combine(kindRoot, copiedName!)));
        Assert.True(File.Exists(Path.Combine(kindRoot, sourceName)));

        // Посторонний старый файл уборка уносит, обе актуальные ссылки — нет.
        var orphanName = await storage.SaveAsync(
            kind, Guid.NewGuid(), Extension(kind), new MemoryStream(new byte[] { 0x01 }));
        var old = DateTime.UtcNow.AddDays(-2);
        File.SetLastWriteTimeUtc(Path.Combine(kindRoot, sourceName), old);
        File.SetLastWriteTimeUtc(Path.Combine(kindRoot, copiedName!), old);
        File.SetLastWriteTimeUtc(Path.Combine(kindRoot, orphanName), old);

        await using var collectorDb = Database.CreateContext();
        var collector = new RecipeGarbageCollector(
            collectorDb, storage, TimeProvider.System, NullLogger<RecipeGarbageCollector>.Instance);
        var cleanup = await collector.CollectAsync(kind, TimeSpan.FromHours(1));

        Assert.Equal(1, cleanup.Deleted);
        Assert.Equal(0, cleanup.Failed);
        Assert.False(File.Exists(Path.Combine(kindRoot, orphanName)));
        Assert.True(File.Exists(Path.Combine(kindRoot, sourceName)));
        Assert.True(File.Exists(Path.Combine(kindRoot, copiedName!)));
    }

    private static Task<RecipeMutationResult> UploadFileAsync(
        RecipeMutationService service, Recipe recipe, Family family, RecipeFileKind kind, byte marker) =>
        kind == RecipeFileKind.Photo
            ? service.UploadPhotoAsync(
                new RecipeTarget(recipe.Id, family.Id), recipe.Revision, ".png", new MemoryStream(new[] { marker }))
            : service.UploadDocumentAsync(
                new RecipeTarget(recipe.Id, family.Id), recipe.Revision, ".pdf", new MemoryStream(new[] { marker }));

    private static string Extension(RecipeFileKind kind) =>
        kind == RecipeFileKind.Photo ? ".png" : ".pdf";

    private static string? GetPath(Recipe recipe, RecipeFileKind kind) =>
        kind == RecipeFileKind.Photo ? recipe.PhotoPath : recipe.DocumentPath;

    private static string KindRoot(string root, RecipeFileKind kind) =>
        Path.Combine(root, kind == RecipeFileKind.Photo ? "photos" : "documents");

    private static RecipeFileLifecycle NewLifecycle(FileStorage store) =>
        new(store, NullLogger<RecipeFileLifecycle>.Instance);

    private static (FileStorage Storage, string Root) NewStorage()
    {
        var root = Path.Combine(
            Path.GetTempPath(), "menu_planner_file_lifecycle_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RECIPE_FILES_PHOTOS_DIR"] = Path.Combine(root, "photos"),
                ["RECIPE_FILES_DOCUMENTS_DIR"] = Path.Combine(root, "documents")
            })
            .Build();
        return (new FileStorage(configuration, NullLogger<FileStorage>.Instance), root);
    }

    private async Task<Family> SeedFamilyAsync()
    {
        var owner = PostgresData.NewUser($"file-{Guid.NewGuid():N}@example.com");
        var family = PostgresData.NewFamily("Семья", $"FL-{Guid.NewGuid().ToString("N")[..8]}", owner.Id);

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

    private async Task SetPathAsync(RecipeFileKind kind, Guid recipeId, string storedName)
    {
        await using var db = Database.CreateContext();
        var recipe = await db.Recipes.SingleAsync(r => r.Id == recipeId);
        if (kind == RecipeFileKind.Photo) recipe.PhotoPath = storedName;
        else recipe.DocumentPath = storedName;
        await db.SaveChangesAsync();
    }
}
