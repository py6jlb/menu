using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Recipes.External;

/// <summary>Исход промоушена внешнего рецепта в собственную копию.</summary>
public enum RecipePromotionOutcome
{
    Promoted,
    NotFound,
    NotExternal,
    MissingRevision,
    Conflict,
    BrokenSource
}

/// <summary>
/// Итог промоушена: при успехе — уже собственная строка Recipe (id сохранён),
/// при конфликте — актуальная серверная ревизия.
/// </summary>
public sealed record RecipePromotionResult(
    RecipePromotionOutcome Outcome,
    Recipe? Recipe = null,
    string? Error = null,
    int Revision = 0);

/// <summary>
/// Однократный промоушен внешнего рецепта в копию на месте. Переход
/// «внешний → собственный» защищён до вставки зависимого контента и файловых
/// эффектов: строка атомарно «выкупается» условным обновлением по ревизии,
/// и только победитель копирует шаги, ингредиенты и фото. Повторный промоушен
/// возвращает предсказуемый конфликт, не повторяя копирование.
/// Scoped-сервис, читает БД и копирует файл фото.
/// </summary>
public sealed class ExternalRecipePromotionService
{
    private readonly AppDbContext _db;
    private readonly SourceFamilyNameResolver _sourceNames;
    private readonly PhotoStorage _storage;
    private readonly TimeProvider _clock;
    private readonly RecipeRevisionReader _revisions;

    public ExternalRecipePromotionService(
        AppDbContext db,
        SourceFamilyNameResolver sourceNames,
        PhotoStorage storage,
        TimeProvider clock,
        RecipeRevisionReader revisions)
    {
        _db = db;
        _sourceNames = sourceNames;
        _storage = storage;
        _clock = clock;
        _revisions = revisions;
    }

    public async Task<RecipePromotionResult> PromoteAsync(
        RecipeTarget target, int? expectedRevision, CancellationToken cancellationToken = default)
    {
        var wrapper = await _db.Recipes
            .AsNoTracking()
            .Where(r => r.Id == target.Id && r.FamilyId == target.FamilyId)
            .Select(r => new { r.SourceRecipeId, r.SourceFamilyId, r.Revision })
            .FirstOrDefaultAsync(cancellationToken);
        if (wrapper is null)
            return new(RecipePromotionOutcome.NotFound);
        if (wrapper.SourceRecipeId is not Guid sourceId)
            return new(RecipePromotionOutcome.NotExternal);
        if (expectedRevision is null)
            return new(RecipePromotionOutcome.MissingRevision, Revision: wrapper.Revision);
        if (!RecipeRevisionRules.IsCurrent(expectedRevision, wrapper.Revision))
            return new(RecipePromotionOutcome.Conflict, Revision: wrapper.Revision);

        var source = await _db.Recipes
            .AsNoTracking()
            .Include(r => r.Family)
            .Include(r => r.Steps)
            .Include(r => r.Ingredients)
            .FirstOrDefaultAsync(r => r.Id == sourceId, cancellationToken);
        if (source is null)
        {
            // Сломанная ссылка: контента нет, спасать нечего.
            return new(RecipePromotionOutcome.BrokenSource,
                Error: "Источник удалил рецепт — копию сделать нельзя.");
        }

        var copiedFromFamilyName = source.Family?.Name
            ?? await _sourceNames.ResolveAsync(wrapper.SourceFamilyId)
            ?? await _sourceNames.ResolveAsync(source.FamilyId);

        return _db.SupportsRelationalLocking()
            ? await PromoteRelationalAsync(
                target, sourceId, wrapper.Revision, source, copiedFromFamilyName, cancellationToken)
            : await PromoteTrackedAsync(target, source, copiedFromFamilyName, cancellationToken);
    }

    /// <summary>
    /// PostgreSQL: сначала условно выкупаем строку по ревизии и снимаем ссылку на
    /// источник, и только потом копируем контент и файл. Проигравший не доходит
    /// до файловых эффектов и вставки шагов/ингредиентов.
    /// </summary>
    private async Task<RecipePromotionResult> PromoteRelationalAsync(
        RecipeTarget target,
        Guid sourceId,
        int observedRevision,
        Recipe source,
        string? copiedFromFamilyName,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var claimed = await _db.Recipes
            .Where(r => r.Id == target.Id && r.FamilyId == target.FamilyId
                && r.SourceRecipeId == sourceId
                && r.Revision == observedRevision)
            .ExecuteUpdateAsync(update => update
                .SetProperty(r => r.SourceRecipeId, (Guid?)null)
                .SetProperty(r => r.SourceFamilyId, (Guid?)null)
                .SetProperty(r => r.SourceToken, (string?)null)
                .SetProperty(r => r.Revision, r => r.Revision + 1), cancellationToken);

        if (claimed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(RecipePromotionOutcome.Conflict, Revision: await _revisions.CurrentAsync(target.Id, cancellationToken));
        }

        string? copiedPhoto = null;
        try
        {
            var wrapper = await _db.Recipes
                .Include(r => r.Steps)
                .Include(r => r.Ingredients)
                .FirstAsync(r => r.Id == target.Id, cancellationToken);

            copiedPhoto = source.PhotoPath is null
                ? null
                : await _storage.CopyAsync(target.Id, source.PhotoPath);

            ApplyCopiedContent(wrapper, source, copiedFromFamilyName, copiedPhoto);
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(RecipePromotionOutcome.Promoted, wrapper, Revision: wrapper.Revision);
        }
        catch
        {
            // Ошибка после начала операции: возвращаем строку внешнему состоянию
            // и убираем уже скопированный файл, чтобы не оставить сироту.
            await transaction.RollbackAsync(cancellationToken);
            _storage.Delete(copiedPhoto);
            throw;
        }
    }

    /// <summary>
    /// In-memory (тесты без PostgreSQL): конкуренции на хранилище нет, поэтому
    /// достаточно проверки внешнего состояния и ревизии с последующей записью.
    /// </summary>
    private async Task<RecipePromotionResult> PromoteTrackedAsync(
        RecipeTarget target, Recipe source, string? copiedFromFamilyName, CancellationToken cancellationToken)
    {
        var wrapper = await _db.Recipes
            .Include(r => r.Steps)
            .Include(r => r.Ingredients)
            .FirstAsync(r => r.Id == target.Id, cancellationToken);
        if (wrapper.SourceRecipeId is null)
            return new(RecipePromotionOutcome.Conflict, Revision: wrapper.Revision);

        string? copiedPhoto = null;
        try
        {
            copiedPhoto = source.PhotoPath is null
                ? null
                : await _storage.CopyAsync(target.Id, source.PhotoPath);

            ApplyCopiedContent(wrapper, source, copiedFromFamilyName, copiedPhoto);
            wrapper.SourceRecipeId = null;
            wrapper.SourceFamilyId = null;
            wrapper.SourceToken = null;
            wrapper.Revision = RecipeRevisionRules.Next(wrapper.Revision);
            await _db.SaveChangesAsync(cancellationToken);
            return new(RecipePromotionOutcome.Promoted, wrapper, Revision: wrapper.Revision);
        }
        catch
        {
            _storage.Delete(copiedPhoto);
            throw;
        }
    }

    private void ApplyCopiedContent(
        Recipe wrapper, Recipe source, string? copiedFromFamilyName, string? copiedPhoto)
    {
        wrapper.Name = source.Name;
        wrapper.Description = source.Description;
        wrapper.CookTimeMinutes = source.CookTimeMinutes;
        wrapper.Servings = source.Servings;
        wrapper.Difficulty = source.Difficulty;
        wrapper.Calories = source.Calories;
        wrapper.Tags = new List<string>(source.Tags);
        wrapper.Seasonality = new List<string>(source.Seasonality);
        wrapper.Diet = DietCatalog.NormalizeAll(source.Diet);
        wrapper.PhotoPath = copiedPhoto;
        wrapper.Steps = source.Steps
            .OrderBy(s => s.Order)
            .Select(s => new RecipeStep { Order = s.Order, Text = s.Text })
            .ToList();
        wrapper.Ingredients = source.Ingredients
            .OrderBy(i => i.Order)
            .Select(i => new RecipeIngredient
            {
                Order = i.Order,
                Name = i.Name,
                Amount = i.Amount,
                Unit = i.Unit,
                Note = i.Note
            })
            .ToList();
        wrapper.CopiedFromFamilyName = copiedFromFamilyName;
        wrapper.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
    }
}
