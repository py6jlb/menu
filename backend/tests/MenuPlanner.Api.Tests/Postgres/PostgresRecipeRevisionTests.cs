using System.Data.Common;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Recipes;
using MenuPlanner.Api.Recipes.External;
using MenuPlanner.Api.Recipes.Photos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MenuPlanner.Api.Tests.Postgres;

/// <summary>
/// Гарантии проверяемой ревизии рецепта на настоящей PostgreSQL: конкурирующие
/// PUT не смешивают содержимое, двойной промоушен создаёт один набор зависимого
/// контента и одно фото, copy/удаление сходятся в согласованное состояние, а
/// ошибка после начала промоушена откатывает «выкуп» строки.
/// Пересечение операций задаётся барьером на самой команде БД, без sleep.
/// </summary>
public sealed class PostgresRecipeRevisionTests : PostgresTestBase
{
    [PostgresFact]
    public async Task ConcurrentPut_OneWins_OneConflicts_WithoutMergingContent()
    {
        var family = await SeedFamilyAsync();
        var recipe = await SeedRecipeAsync(family.Id, "Борщ");
        var barrier = new AsyncBarrier(2);

        async Task<RecipeMutationResult> UpdateAsync(string name)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(Database.ConnectionString)
                .AddInterceptors(new BarrierNonQueryInterceptor(barrier, "UPDATE \"Recipes\""))
                .Options;
            await using var db = new AppDbContext(options);
            var service = new RecipeMutationService(db, NewLifecycle(), TimeProvider.System, new RecipeRevisionReader(db));
            return await service.UpdateAsync(new RecipeTarget(recipe.Id, family.Id), Request(name, recipe.Revision));
        }

        var results = await Task.WhenAll(UpdateAsync("Первый"), UpdateAsync("Второй"));

        Assert.Single(results, r => r.Outcome == RecipeMutationOutcome.Ok);
        var conflicted = Assert.Single(results, r => r.Outcome == RecipeMutationOutcome.Conflict);
        Assert.Equal(2, conflicted.Revision);

        await using var verify = Database.CreateContext();
        var stored = await verify.Recipes.Include(r => r.Steps).SingleAsync(r => r.Id == recipe.Id);
        Assert.Equal(2, stored.Revision);
        // Победил ровно один полный запрос, а не смесь двух.
        Assert.Contains(stored.Name, new[] { "Первый", "Второй" });
        Assert.Single(stored.Steps);
    }

    [PostgresFact]
    public async Task StalePut_DoesNotOverwriteNewerRevision()
    {
        var family = await SeedFamilyAsync();
        var recipe = await SeedRecipeAsync(family.Id, "Борщ");

        await using (var db = Database.CreateContext())
        {
            var service = new RecipeMutationService(db, NewLifecycle(), TimeProvider.System, new RecipeRevisionReader(db));
            var first = await service.UpdateAsync(new RecipeTarget(recipe.Id, family.Id), Request("Первая", recipe.Revision));
            Assert.Equal(RecipeMutationOutcome.Ok, first.Outcome);
        }

        await using (var db = Database.CreateContext())
        {
            var service = new RecipeMutationService(db, NewLifecycle(), TimeProvider.System, new RecipeRevisionReader(db));
            // Ожидаемая ревизия уже устарела — правка отклоняется.
            var stale = await service.UpdateAsync(new RecipeTarget(recipe.Id, family.Id), Request("Устаревшая", recipe.Revision));
            Assert.Equal(RecipeMutationOutcome.Conflict, stale.Outcome);
            Assert.Equal(2, stale.Revision);
        }

        await using var verify = Database.CreateContext();
        var stored = await verify.Recipes.SingleAsync(r => r.Id == recipe.Id);
        Assert.Equal("Первая", stored.Name);
        Assert.Equal(2, stored.Revision);
    }

    [PostgresFact]
    public async Task ConcurrentPromotion_ClaimsOnce_AndCopiesContentAndPhotoOnce()
    {
        var sourceFamily = await SeedFamilyAsync();
        var source = await SeedRecipeAsync(sourceFamily.Id, "Борщ", withDetails: true);
        var (storage, root) = NewStorageWithRoot();
        var photoName = await storage.SaveAsync(
            source.Id, ".png", new MemoryStream(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));
        await SetSourcePhotoAsync(source.Id, photoName);

        var recipientFamily = await SeedFamilyAsync();
        var wrapper = await SeedWrapperAsync(recipientFamily.Id, source, sourceFamily.Id);
        var barrier = new AsyncBarrier(2);

        async Task<RecipePromotionResult> PromoteAsync()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(Database.ConnectionString)
                .AddInterceptors(new BarrierNonQueryInterceptor(barrier, "UPDATE \"Recipes\""))
                .Options;
            await using var db = new AppDbContext(options);
            var service = new ExternalRecipePromotionService(
                db, new SourceFamilyNameResolver(db), NewLifecycle(storage), TimeProvider.System, new RecipeRevisionReader(db));
            return await service.PromoteAsync(new RecipeTarget(wrapper.Id, recipientFamily.Id), wrapper.Revision);
        }

        var results = await Task.WhenAll(PromoteAsync(), PromoteAsync());

        Assert.Single(results, r => r.Outcome == RecipePromotionOutcome.Promoted);
        Assert.Single(results, r => r.Outcome == RecipePromotionOutcome.Conflict);

        await using var verify = Database.CreateContext();
        var stored = await verify.Recipes
            .Include(r => r.Steps)
            .Include(r => r.Ingredients)
            .SingleAsync(r => r.Id == wrapper.Id);
        Assert.Null(stored.SourceRecipeId);
        Assert.Equal(2, stored.Steps.Count);
        Assert.Single(stored.Ingredients);
        Assert.NotNull(stored.CopiedFromFamilyName);

        var copiedFiles = Directory.GetFiles(root, $"{wrapper.Id:N}-*");
        Assert.Single(copiedFiles);
    }

    [PostgresFact]
    public async Task LocalRemove_WithStaleRevision_Conflicts_ThenRemovesWithCurrent()
    {
        var sourceFamily = await SeedFamilyAsync();
        var source = await SeedRecipeAsync(sourceFamily.Id, "Борщ", withDetails: true);
        var recipientFamily = await SeedFamilyAsync();
        var wrapper = await SeedWrapperAsync(recipientFamily.Id, source, sourceFamily.Id);
        var storage = NewStorage();

        await using (var db = Database.CreateContext())
        {
            var mutations = new RecipeMutationService(db, NewLifecycle(storage), TimeProvider.System, new RecipeRevisionReader(db));
            var stale = await mutations.RemoveExternalAsync(new RecipeTarget(wrapper.Id, recipientFamily.Id), wrapper.Revision + 1);
            Assert.Equal(RecipeMutationOutcome.Conflict, stale.Outcome);
            Assert.Equal(wrapper.Revision, stale.Revision);
        }

        await using (var db = Database.CreateContext())
        {
            var mutations = new RecipeMutationService(db, NewLifecycle(storage), TimeProvider.System, new RecipeRevisionReader(db));
            var current = await mutations.RemoveExternalAsync(new RecipeTarget(wrapper.Id, recipientFamily.Id), wrapper.Revision);
            Assert.Equal(RecipeMutationOutcome.Ok, current.Outcome);
        }

        await using var verify = Database.CreateContext();
        Assert.False(await verify.Recipes.AnyAsync(r => r.Id == wrapper.Id));
    }

    [PostgresFact]
    public async Task PromotionThenLocalRemove_IsRejectedBecauseNoLongerExternal()
    {
        var sourceFamily = await SeedFamilyAsync();
        var source = await SeedRecipeAsync(sourceFamily.Id, "Борщ", withDetails: true);
        var recipientFamily = await SeedFamilyAsync();
        var wrapper = await SeedWrapperAsync(recipientFamily.Id, source, sourceFamily.Id);
        var storage = NewStorage();

        await using (var db = Database.CreateContext())
        {
            var promotion = new ExternalRecipePromotionService(
                db, new SourceFamilyNameResolver(db), NewLifecycle(storage), TimeProvider.System, new RecipeRevisionReader(db));
            var promoted = await promotion.PromoteAsync(new RecipeTarget(wrapper.Id, recipientFamily.Id), wrapper.Revision);
            Assert.Equal(RecipePromotionOutcome.Promoted, promoted.Outcome);

            // Промоушен сделал обёртку собственной: локальное удаление внешнего больше не применимо.
            var mutations = new RecipeMutationService(db, NewLifecycle(storage), TimeProvider.System, new RecipeRevisionReader(db));
            var remove = await mutations.RemoveExternalAsync(
                new RecipeTarget(wrapper.Id, recipientFamily.Id), promoted.Revision);
            Assert.Equal(RecipeMutationOutcome.ValidationError, remove.Outcome);
        }

        await using var verify = Database.CreateContext();
        var stored = await verify.Recipes
            .Include(r => r.Steps)
            .SingleAsync(r => r.Id == wrapper.Id);
        Assert.Null(stored.SourceRecipeId);
        Assert.Equal(2, stored.Steps.Count);
    }

    [PostgresFact]
    public async Task ErrorAfterClaimStart_RollsBackAndKeepsWrapperExternal()
    {
        var sourceFamily = await SeedFamilyAsync();
        var source = await SeedRecipeAsync(sourceFamily.Id, "Борщ", withDetails: true);
        var recipientFamily = await SeedFamilyAsync();
        var wrapper = await SeedWrapperAsync(recipientFamily.Id, source, sourceFamily.Id);
        var storage = NewStorage();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Database.ConnectionString)
            .AddInterceptors(new ThrowOnCommandInterceptor("INSERT INTO \"RecipeSteps\""))
            .Options;

        await using (var db = new AppDbContext(options))
        {
            var service = new ExternalRecipePromotionService(
                db, new SourceFamilyNameResolver(db), NewLifecycle(storage), TimeProvider.System, new RecipeRevisionReader(db));
            // EF оборачивает сбой команды в DbUpdateException; важно, что операция падает,
            // а откат возвращает строку внешнему состоянию.
            await Assert.ThrowsAsync<DbUpdateException>(
                () => service.PromoteAsync(new RecipeTarget(wrapper.Id, recipientFamily.Id), wrapper.Revision));
        }

        await using var verify = Database.CreateContext();
        var stored = await verify.Recipes.Include(r => r.Steps).SingleAsync(r => r.Id == wrapper.Id);
        // Откат вернул строку внешнему состоянию и исходной ревизии.
        Assert.Equal(wrapper.Revision, stored.Revision);
        Assert.Equal(source.Id, stored.SourceRecipeId);
        Assert.Empty(stored.Steps);
        Assert.Empty(await verify.RecipeIngredients.Where(i => i.RecipeId == wrapper.Id).ToListAsync());
    }

    private static RecipeRequest Request(string name, int revision) => new(
        Name: name,
        Description: null,
        CookTimeMinutes: 10,
        Servings: 2,
        Difficulty: 1,
        Calories: null,
        Tags: new List<string>(),
        Seasonality: new List<string>(),
        Diet: new List<string>(),
        Steps: new List<RecipeStepRequest> { new("Сварить.") },
        Ingredients: new List<RecipeIngredientRequest> { new("Соль", 1m, "g", null) },
        Revision: revision);

    private async Task<Family> SeedFamilyAsync(string name = "Семья")
    {
        var owner = PostgresData.NewUser($"rev-{Guid.NewGuid():N}@example.com");
        var family = PostgresData.NewFamily(name, $"REV-{Guid.NewGuid().ToString("N")[..8]}", owner.Id);

        await using var db = Database.CreateContext();
        db.Users.Add(owner);
        db.Families.Add(family);
        await db.SaveChangesAsync();
        return family;
    }

    private async Task<Recipe> SeedRecipeAsync(Guid familyId, string name, bool withDetails = false)
    {
        var recipe = PostgresData.NewRecipe(familyId, name);
        if (withDetails)
        {
            recipe.Steps = new List<RecipeStep>
            {
                new() { Order = 0, Text = "Сварить бульон." },
                new() { Order = 1, Text = "Добавить свёклу." }
            };
            recipe.Ingredients = new List<RecipeIngredient>
            {
                new() { Order = 0, Name = "Свёкла", Amount = 2m, Unit = "pcs" }
            };
        }

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

    private async Task SetSourcePhotoAsync(Guid recipeId, string photoName)
    {
        await using var db = Database.CreateContext();
        var recipe = await db.Recipes.SingleAsync(r => r.Id == recipeId);
        recipe.PhotoPath = photoName;
        await db.SaveChangesAsync();
    }

    private static PhotoLifecycle NewLifecycle() => NewLifecycle(NewStorage());

    private static PhotoLifecycle NewLifecycle(PhotoStorage store) =>
        new(store, NullLogger<PhotoLifecycle>.Instance);

    private static PhotoStorage NewStorage() => NewStorageWithRoot().Storage;

    private static (PhotoStorage Storage, string Root) NewStorageWithRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "menu_planner_rev_photos_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PHOTOS_DIR"] = root })
            .Build();
        return (new PhotoStorage(configuration), root);
    }

    /// <summary>Роняет операцию на выбранной команде БД, чтобы проверить откат.</summary>
    private sealed class ThrowOnCommandInterceptor : DbCommandInterceptor
    {
        private readonly string _fragment;

        public ThrowOnCommandInterceptor(string fragment) => _fragment = fragment;

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            ThrowIfMatch(command);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfMatch(command);
            return new ValueTask<InterceptionResult<int>>(result);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            ThrowIfMatch(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfMatch(command);
            return new ValueTask<InterceptionResult<DbDataReader>>(result);
        }

        private void ThrowIfMatch(DbCommand command)
        {
            if (command.CommandText.Contains(_fragment, StringComparison.Ordinal))
                throw new InvalidOperationException("Симуляция сбоя после начала операции.");
        }
    }
}
