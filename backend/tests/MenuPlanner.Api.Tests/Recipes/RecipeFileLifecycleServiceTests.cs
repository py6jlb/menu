using System.Text;
using MenuPlanner.Infrastructure.Data;
using MenuPlanner.Api.Recipes;
using MenuPlanner.Api.Recipes.Crud;
using MenuPlanner.Api.Recipes.External;
using MenuPlanner.Api.Recipes.Files;
using MenuPlanner.Api.Recipes.Reading;
using MenuPlanner.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using MenuPlanner.Domain.Accounts;
using MenuPlanner.Domain.Families;
using MenuPlanner.Domain.Recipes;

namespace MenuPlanner.Api.Tests.Recipes;

/// <summary>
/// Жизненный цикл файла рецепта (фото и документ) у сервисов правки и промоушена:
/// неуспех записи БД убирает новый файл, а ошибка удаления старого файла после
/// commit не превращает уже завершённое изменение в ложный общий сбой (файл
/// остаётся кандидатом уборки). Поведение одинаково для обоих видов.
/// </summary>
public sealed class RecipeFileLifecycleServiceTests
{
    [Theory]
    [InlineData(RecipeFileKind.Photo)]
    [InlineData(RecipeFileKind.Document)]
    public async Task Upload_WhenDbWriteFails_DiscardsStagedFile(RecipeFileKind kind)
    {
        var store = new InMemoryRecipeFileStore();
        var dbName = "file-upload-db-fail-" + kind;
        Guid recipeId;
        await using (var seed = NewDb(dbName))
        {
            var (seeded, _) = await SeedRecipeAsync(seed);
            recipeId = seeded.Id;
        }

        await using var db = NewDb(dbName, new ThrowOnRecipeFileUpdateInterceptor(PathProperty(kind)));
        var recipe = await db.Recipes.SingleAsync(r => r.Id == recipeId);
        var service = NewService(db, store);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UploadAsync(service, new RecipeTarget(recipe.Id, recipe.FamilyId), kind, recipe.Revision));

        // Новый файл убран компенсацией, БД осталась прежней.
        Assert.Empty(store.Names(kind));
        await using var verify = NewDb(dbName);
        var stored = await verify.Recipes.SingleAsync(r => r.Id == recipeId);
        Assert.Null(GetPath(stored, kind));
        Assert.Equal(1, stored.Revision);
    }

    [Theory]
    [InlineData(RecipeFileKind.Photo)]
    [InlineData(RecipeFileKind.Document)]
    public async Task Upload_WhenPreviousDeleteFails_StillCompletes_AndKeepsOldAsCandidate(RecipeFileKind kind)
    {
        var store = new InMemoryRecipeFileStore();
        var oldName = OldName(kind);
        store.Put(kind, oldName, Encoding.ASCII.GetBytes("old"));
        store.FailingDelete = (k, name) => k == kind && name == oldName;

        await using var db = NewDb("file-upload-retire-" + kind);
        var (recipe, family) = await SeedRecipeAsync(db, kind, oldName);
        var service = NewService(db, store);

        var result = await UploadAsync(service, new RecipeTarget(recipe.Id, family.Id), kind, recipe.Revision);

        Assert.Equal(RecipeMutationOutcome.Ok, result.Outcome);
        // Завершённое изменение не стало неуспешным; старый файл остался уборке.
        Assert.True(store.Contains(kind, oldName));
        Assert.Equal(2, store.Names(kind).Count);
        var stored = await db.Recipes.SingleAsync(r => r.Id == recipe.Id);
        Assert.Equal(2, stored.Revision);
        Assert.Equal(GetPath(result.Recipe!, kind), GetPath(stored, kind));
        Assert.True(store.Contains(kind, GetPath(stored, kind)!));
    }

    [Theory]
    [InlineData(RecipeFileKind.Photo)]
    [InlineData(RecipeFileKind.Document)]
    public async Task Delete_WhenFileDeleteFails_StillCompletes(RecipeFileKind kind)
    {
        var store = new InMemoryRecipeFileStore();
        var oldName = OldName(kind);
        store.Put(kind, oldName, Encoding.ASCII.GetBytes("old"));
        store.FailingDelete = (k, name) => k == kind && name == oldName;

        await using var db = NewDb("file-delete-retire-" + kind);
        var (recipe, family) = await SeedRecipeAsync(db, kind, oldName);
        var service = NewService(db, store);

        var result = await DeleteAsync(service, new RecipeTarget(recipe.Id, family.Id), kind, recipe.Revision);

        Assert.Equal(RecipeMutationOutcome.Ok, result.Outcome);
        Assert.True(store.Contains(kind, oldName));
        var stored = await db.Recipes.SingleAsync(r => r.Id == recipe.Id);
        Assert.Null(GetPath(stored, kind));
        Assert.Equal(2, stored.Revision);
    }

    [Theory]
    [InlineData(RecipeFileKind.Photo)]
    [InlineData(RecipeFileKind.Document)]
    public async Task Promotion_WhenDbWriteFails_DiscardsCopiedFile(RecipeFileKind kind)
    {
        var store = new InMemoryRecipeFileStore();
        var oldName = OldName(kind);
        store.Put(kind, oldName, Encoding.ASCII.GetBytes("source"));

        var dbName = "file-promotion-fail-" + kind;
        Guid wrapperId;
        Guid recipientFamilyId;
        await using (var seed = NewDb(dbName))
        {
            var sourceFamily = NewFamily();
            var source = NewRecipe(sourceFamily.Id, "Источник");
            SetPath(source, kind, oldName);
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

        await using var db = NewDb(dbName, new ThrowOnRecipeFileUpdateInterceptor(PathProperty(kind)));
        var promotion = new ExternalRecipePromotionService(
            db,
            new SourceFamilyNameResolver(db),
            new RecipeFileLifecycle(store, NullLogger<RecipeFileLifecycle>.Instance),
            TimeProvider.System,
            new RecipeRevisionReader(db));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            promotion.PromoteAsync(new RecipeTarget(wrapperId, recipientFamilyId), 1));

        // Скопированный файл убран компенсацией, файл источника не тронут.
        Assert.Equal(new[] { oldName }, store.Names(kind));
    }

    [Theory]
    [InlineData(RecipeFileKind.Photo)]
    [InlineData(RecipeFileKind.Document)]
    public async Task ReplaceAsync_WhenCommitConfirmed_RetiresPrevious_AndKeepsNew(RecipeFileKind kind)
    {
        var store = new InMemoryRecipeFileStore();
        var oldName = OldName(kind);
        store.Put(kind, oldName, Encoding.ASCII.GetBytes("old"));
        var lifecycle = new RecipeFileLifecycle(store, NullLogger<RecipeFileLifecycle>.Instance);

        var created = await lifecycle.ReplaceAsync(
            kind, Guid.NewGuid(), Extension(kind), new MemoryStream(new byte[] { 7 }), oldName,
            staged => Task.FromResult(new RecipeFileCommit<string>(true, staged)));

        Assert.False(store.Contains(kind, oldName));
        Assert.Single(store.Names(kind));
        Assert.True(store.Contains(kind, created));
    }

    [Theory]
    [InlineData(RecipeFileKind.Photo)]
    [InlineData(RecipeFileKind.Document)]
    public async Task ReplaceAsync_WhenNotCommitted_DiscardsNewFile_AndKeepsPrevious(RecipeFileKind kind)
    {
        var store = new InMemoryRecipeFileStore();
        var oldName = OldName(kind);
        store.Put(kind, oldName, Encoding.ASCII.GetBytes("old"));
        var lifecycle = new RecipeFileLifecycle(store, NullLogger<RecipeFileLifecycle>.Instance);

        await lifecycle.ReplaceAsync(
            kind, Guid.NewGuid(), Extension(kind), new MemoryStream(new byte[] { 7 }), oldName,
            staged => Task.FromResult(new RecipeFileCommit<string>(false, staged)));

        Assert.True(store.Contains(kind, oldName));
        Assert.Single(store.Names(kind));
    }

    [Theory]
    [InlineData(RecipeFileKind.Photo)]
    [InlineData(RecipeFileKind.Document)]
    public void Discard_WhenDeleteFails_DoesNotThrow_AndKeepsFileAsCandidate(RecipeFileKind kind)
    {
        var store = new InMemoryRecipeFileStore();
        var oldName = OldName(kind);
        store.Put(kind, oldName, Encoding.ASCII.GetBytes("old"));
        store.FailingDelete = (k, name) => k == kind && name == oldName;
        var lifecycle = new RecipeFileLifecycle(store, NullLogger<RecipeFileLifecycle>.Instance);

        // Сбой компенсации не маскирует исходную ошибку и оставляет файл уборке.
        lifecycle.Discard(kind, oldName);

        Assert.True(store.Contains(kind, oldName));
    }

    private static string Extension(RecipeFileKind kind) =>
        kind == RecipeFileKind.Photo ? ".png" : ".pdf";

    private static string OldName(RecipeFileKind kind) =>
        $"0123456789abcdef0123456789abcdef-0123456789abcdef0123456789abcdef{Extension(kind)}";

    private static string PathProperty(RecipeFileKind kind) =>
        kind == RecipeFileKind.Photo ? "PhotoPath" : "DocumentPath";

    private static void SetPath(Recipe recipe, RecipeFileKind kind, string? name)
    {
        if (kind == RecipeFileKind.Photo) recipe.PhotoPath = name;
        else recipe.DocumentPath = name;
    }

    private static string? GetPath(Recipe recipe, RecipeFileKind kind) =>
        kind == RecipeFileKind.Photo ? recipe.PhotoPath : recipe.DocumentPath;

    private static Task<RecipeMutationResult> UploadAsync(
        RecipeMutationService service, RecipeTarget target, RecipeFileKind kind, int? revision) =>
        kind == RecipeFileKind.Photo
            ? service.UploadPhotoAsync(target, revision, ".png", new MemoryStream(new byte[] { 1, 2, 3 }))
            : service.UploadDocumentAsync(target, revision, ".pdf", new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7")));

    private static Task<RecipeMutationResult> DeleteAsync(
        RecipeMutationService service, RecipeTarget target, RecipeFileKind kind, int? revision) =>
        kind == RecipeFileKind.Photo
            ? service.DeletePhotoAsync(target, revision)
            : service.DeleteDocumentAsync(target, revision);

    private static RecipeMutationService NewService(AppDbContext db, IRecipeFileStore store) =>
        new(db,
            new RecipeFileLifecycle(store, NullLogger<RecipeFileLifecycle>.Instance),
            TimeProvider.System,
            new RecipeRevisionReader(db));

    private static AppDbContext NewDb(string name, params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name);
        if (interceptors.Length > 0) builder.AddInterceptors(interceptors);
        return new AppDbContext(builder.Options);
    }

    private static async Task<(Recipe Recipe, Family Family)> SeedRecipeAsync(
        AppDbContext db, RecipeFileKind? kind = null, string? path = null)
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
        if (kind is { } fileKind) SetPath(recipe, fileKind, path);
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

    /// <summary>Роняет запись ссылки вида у рецепта, имитируя отказ БД после записи файла.</summary>
    private sealed class ThrowOnRecipeFileUpdateInterceptor : SaveChangesInterceptor
    {
        private readonly string _propertyName;

        public ThrowOnRecipeFileUpdateInterceptor(string propertyName) => _propertyName = propertyName;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is { } context
                && context.ChangeTracker.Entries<Recipe>().Any(entry =>
                    entry.State == EntityState.Modified
                    && entry.Property(_propertyName).IsModified))
            {
                throw new InvalidOperationException("Симуляция отказа БД после записи файла.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
