using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Recipes;

/// <summary>Исход правки/удаления рецепта с проверяемой ревизией.</summary>
public enum RecipeMutationOutcome
{
    Ok,
    NotFound,
    ExternalReadOnly,
    MissingRevision,
    Conflict,
    ValidationError
}

/// <summary>
/// Результат операции: исход, при успехе — актуальный рецепт, при конфликте —
/// серверная ревизия для сравнения. <see cref="Revision"/> при успешном удалении
/// равна следующей ревизии (записи уже нет).
/// </summary>
public sealed record RecipeMutationResult(
    RecipeMutationOutcome Outcome,
    Recipe? Recipe = null,
    string? Error = null,
    int Revision = 0);

/// <summary>
/// Правка и удаление собственного рецепта с атомарной проверкой ревизии.
/// Ревизия — токен конкурентности EF Core: два запроса, прочитавшие одну версию,
/// не могут оба её перезаписать; проигравший получает согласованный конфликт,
/// а не смешивает содержимое. Scoped-сервис, читает БД.
/// </summary>
public sealed class RecipeMutationService
{
    private readonly AppDbContext _db;
    private readonly PhotoStorage _storage;
    private readonly TimeProvider _clock;

    public RecipeMutationService(AppDbContext db, PhotoStorage storage, TimeProvider clock)
    {
        _db = db;
        _storage = storage;
        _clock = clock;
    }

    public async Task<RecipeMutationResult> UpdateAsync(
        Guid id, Guid familyId, RecipeRequest request, CancellationToken cancellationToken = default)
    {
        var recipe = await _db.Recipes
            .Include(r => r.Steps)
            .Include(r => r.Ingredients)
            .FirstOrDefaultAsync(r => r.Id == id && r.FamilyId == familyId, cancellationToken);
        if (recipe is null)
            return new(RecipeMutationOutcome.NotFound);
        if (recipe.SourceRecipeId is not null)
            return new(RecipeMutationOutcome.ExternalReadOnly);

        var error = RecipeValidation.Validate(request);
        if (error is not null)
            return new(RecipeMutationOutcome.ValidationError, Error: error);

        if (request.Revision is null)
            return new(RecipeMutationOutcome.MissingRevision, Revision: recipe.Revision);
        if (!RecipeRevisionRules.IsCurrent(request.Revision, recipe.Revision))
            return new(RecipeMutationOutcome.Conflict, Revision: recipe.Revision);

        RecipeValidation.Apply(recipe, request);
        // Правка рецепта стирает метку происхождения «скопировано из семьи X».
        recipe.CopiedFromFamilyName = null;
        recipe.Revision = RecipeRevisionRules.Next(recipe.Revision);
        recipe.UpdatedAt = _clock.GetUtcNow().UtcDateTime;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(RecipeMutationOutcome.Conflict, Revision: await CurrentRevisionAsync(id, cancellationToken));
        }

        return new(RecipeMutationOutcome.Ok, Recipe: recipe, Revision: recipe.Revision);
    }

    public async Task<RecipeMutationResult> DeleteAsync(
        Guid id, Guid familyId, int? revision, CancellationToken cancellationToken = default)
    {
        var recipe = await _db.Recipes
            .FirstOrDefaultAsync(r => r.Id == id && r.FamilyId == familyId, cancellationToken);
        if (recipe is null)
            return new(RecipeMutationOutcome.NotFound);
        if (recipe.SourceRecipeId is not null)
            return new(RecipeMutationOutcome.ExternalReadOnly);

        return await DeleteTrackedAsync(recipe, revision, cancellationToken);
    }

    /// <summary>
    /// Локальное удаление внешнего рецепта из своей семьи. Источник не затрагивается.
    /// Ревизия защищает и этот путь: устаревшее удаление не убирает чужую правку.
    /// </summary>
    public async Task<RecipeMutationResult> RemoveExternalAsync(
        Guid id, Guid familyId, int? revision, CancellationToken cancellationToken = default)
    {
        var recipe = await _db.Recipes
            .FirstOrDefaultAsync(r => r.Id == id && r.FamilyId == familyId, cancellationToken);
        if (recipe is null)
            return new(RecipeMutationOutcome.NotFound);
        if (recipe.SourceRecipeId is null)
            return new(RecipeMutationOutcome.ValidationError, Error: "Это не внешний рецепт.");

        return await DeleteTrackedAsync(recipe, revision, cancellationToken);
    }

    /// <summary>
    /// Общий шаг удаления с проверкой ревизии: и собственный, и внешний рецепт
    /// проходят одну и ту же защиту от устаревшего удаления.
    /// </summary>
    private async Task<RecipeMutationResult> DeleteTrackedAsync(
        Recipe recipe, int? revision, CancellationToken cancellationToken)
    {
        if (revision is null)
            return new(RecipeMutationOutcome.MissingRevision, Revision: recipe.Revision);
        if (!RecipeRevisionRules.IsCurrent(revision, recipe.Revision))
            return new(RecipeMutationOutcome.Conflict, Revision: recipe.Revision);

        // У внешнего рецепта фото нет: удаление файла — no-op.
        var photoPath = recipe.PhotoPath;
        _db.Recipes.Remove(recipe);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(RecipeMutationOutcome.Conflict, Revision: await CurrentRevisionAsync(recipe.Id, cancellationToken));
        }

        _storage.Delete(photoPath);
        return new(RecipeMutationOutcome.Ok, Revision: RecipeRevisionRules.Next(revision.Value));
    }

    private async Task<int> CurrentRevisionAsync(Guid id, CancellationToken cancellationToken) =>
        await _db.Recipes
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => (int?)r.Revision)
            .FirstOrDefaultAsync(cancellationToken) ?? 0;
}
