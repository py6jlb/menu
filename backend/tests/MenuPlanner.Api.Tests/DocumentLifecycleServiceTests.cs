using System.Text;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Recipes;
using MenuPlanner.Api.Recipes.Documents;
using MenuPlanner.Api.Recipes.External;
using MenuPlanner.Api.Recipes.Photos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MenuPlanner.Api.Tests;

/// <summary>
/// Жизненный цикл PDF-документа у сервисов правки и промоушена: неуспех записи БД
/// убирает новый файл, а промоушен копирует документ источника под своим именем.
/// </summary>
public sealed class DocumentLifecycleServiceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "menu_planner_doc_lifecycle_" + Guid.NewGuid().ToString("N"));

    private DocumentStorage NewStorage()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DOCUMENTS_DIR"] = _root })
            .Build();
        return new DocumentStorage(configuration);
    }

    [Fact]
    public async Task UploadDocument_WhenDbWriteFails_DiscardsStagedFile()
    {
        const string dbName = "document-upload-db-fail";
        Guid recipeId;
        Guid familyId;
        await using (var seed = NewDb(dbName))
        {
            var (seeded, family) = await SeedRecipeAsync(seed);
            recipeId = seeded.Id;
            familyId = family.Id;
        }

        var storage = NewStorage();
        await using var db = NewDb(dbName, new ThrowOnRecipeDocumentUpdateInterceptor());
        var recipe = await db.Recipes.SingleAsync(r => r.Id == recipeId);
        var service = new RecipeMutationService(
            db,
            new PhotoLifecycle(new InMemoryPhotoStore(), NullLogger<PhotoLifecycle>.Instance),
            new DocumentLifecycle(storage, NullLogger<DocumentLifecycle>.Instance),
            TimeProvider.System,
            new RecipeRevisionReader(db));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UploadDocumentAsync(
                new RecipeTarget(recipeId, familyId),
                recipe.Revision,
                ".pdf",
                new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7"))));

        // Новый файл убран компенсацией, БД осталась прежней.
        Assert.Empty(Directory.GetFiles(_root));
        await using var verify = NewDb(dbName);
        var stored = await verify.Recipes.SingleAsync(r => r.Id == recipeId);
        Assert.Null(stored.DocumentPath);
        Assert.Equal(1, stored.Revision);
    }

    [Fact]
    public async Task Promotion_CopiesSourceDocument_ToOwnName()
    {
        var storage = NewStorage();
        var sourceDocument = await storage.SaveAsync(
            Guid.NewGuid(), ".pdf", new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7 source")));

        const string dbName = "document-promotion-copy";
        Guid wrapperId;
        Guid recipientFamilyId;
        await using (var seed = NewDb(dbName))
        {
            var sourceFamily = NewFamily();
            var source = NewRecipe(sourceFamily.Id, "Источник");
            source.DocumentPath = sourceDocument;
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

        await using var db = NewDb(dbName);
        var promotion = new ExternalRecipePromotionService(
            db,
            new SourceFamilyNameResolver(db),
            new PhotoLifecycle(new InMemoryPhotoStore(), NullLogger<PhotoLifecycle>.Instance),
            new DocumentLifecycle(storage, NullLogger<DocumentLifecycle>.Instance),
            TimeProvider.System,
            new RecipeRevisionReader(db));

        var result = await promotion.PromoteAsync(new RecipeTarget(wrapperId, recipientFamilyId), 1);

        Assert.Equal(RecipePromotionOutcome.Promoted, result.Outcome);
        var stored = await db.Recipes.SingleAsync(r => r.Id == wrapperId);
        Assert.NotNull(stored.DocumentPath);
        Assert.NotEqual(sourceDocument, stored.DocumentPath);
        Assert.StartsWith($"{wrapperId:N}-", stored.DocumentPath);
        Assert.True(File.Exists(Path.Combine(_root, stored.DocumentPath!)));
        // Файл источника не тронут: он принадлежит семье-источнику.
        Assert.True(File.Exists(Path.Combine(_root, sourceDocument)));
    }

    private static AppDbContext NewDb(string name, params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name);
        if (interceptors.Length > 0) builder.AddInterceptors(interceptors);
        return new AppDbContext(builder.Options);
    }

    private static async Task<(Recipe Recipe, Family Family)> SeedRecipeAsync(AppDbContext db)
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

    /// <summary>Роняет запись документа рецепта, имитируя отказ БД после записи файла.</summary>
    private sealed class ThrowOnRecipeDocumentUpdateInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is { } context
                && context.ChangeTracker.Entries<Recipe>().Any(entry =>
                    entry.State == EntityState.Modified
                    && entry.Property(r => r.DocumentPath).IsModified))
            {
                throw new InvalidOperationException("Симуляция отказа БД после записи файла.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
            }
        }
    }
}
