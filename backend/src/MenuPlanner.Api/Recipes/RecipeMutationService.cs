using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Recipes.Photos;

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
    int Revision = 0,
    RecipeFieldError? Validation = null);

/// <summary>
/// Правка, удаление и операции с фото собственного рецепта с атомарной проверкой
/// ревизии. Ревизия — токен конкурентности EF Core: два запроса, прочитавшие одну
/// версию, не могут оба её перезаписать; проигравший получает согласованный
/// конфликт, а не смешивает содержимое. Scoped-сервис, читает БД и работает с
/// файлом фото.
/// </summary>
public sealed class RecipeMutationService
{
    private readonly AppDbContext _db;
    private readonly PhotoLifecycle _photos;
    private readonly TimeProvider _clock;
    private readonly RecipeRevisionReader _revisions;

    public RecipeMutationService(
        AppDbContext db, PhotoLifecycle photos, TimeProvider clock, RecipeRevisionReader revisions)
    {
        _db = db;
        _photos = photos;
        _clock = clock;
        _revisions = revisions;
    }

    /// <summary>Создание рецепта семьи из проверенного запроса.</summary>
    public async Task<RecipeMutationResult> CreateAsync(
        Guid familyId, RecipeRequest request, CancellationToken cancellationToken = default)
    {
        var error = RecipeValidation.Validate(request);
        if (error is not null)
            return new(RecipeMutationOutcome.ValidationError, Error: error.Message, Validation: error);

        var now = _clock.GetUtcNow().UtcDateTime;
        var recipe = new Recipe
        {
            FamilyId = familyId,
            Name = "",
            CreatedAt = now,
            UpdatedAt = now
        };
        RecipeValidation.Apply(recipe, request);

        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync(cancellationToken);

        return new(RecipeMutationOutcome.Ok, Recipe: recipe, Revision: recipe.Revision);
    }

    public async Task<RecipeMutationResult> UpdateAsync(
        RecipeTarget target, RecipeRequest request, CancellationToken cancellationToken = default)
    {
        var (recipe, failure) = await FindAsync(
            target, requireOwn: true, includeContent: true, cancellationToken);
        if (failure is not null)
            return failure;

        var error = RecipeValidation.Validate(request);
        if (error is not null)
            return new(RecipeMutationOutcome.ValidationError, Error: error.Message, Validation: error);

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
            return new(RecipeMutationOutcome.Conflict, Revision: await _revisions.CurrentAsync(target.Id, cancellationToken));
        }

        return new(RecipeMutationOutcome.Ok, Recipe: recipe, Revision: recipe.Revision);
    }

    public async Task<RecipeMutationResult> DeleteAsync(
        RecipeTarget target, int? revision, CancellationToken cancellationToken = default)
    {
        var (recipe, failure) = await FindAsync(
            target, requireOwn: true, includeContent: false, cancellationToken);
        if (failure is not null)
            return failure;

        return await DeleteTrackedAsync(recipe, revision, cancellationToken);
    }

    /// <summary>
    /// Локальное удаление внешнего рецепта из своей семьи. Источник не затрагивается.
    /// Ревизия защищает и этот путь: устаревшее удаление не убирает чужую правку.
    /// </summary>
    public async Task<RecipeMutationResult> RemoveExternalAsync(
        RecipeTarget target, int? revision, CancellationToken cancellationToken = default)
    {
        var (recipe, failure) = await FindAsync(
            target, requireOwn: false, includeContent: false, cancellationToken);
        if (failure is not null)
            return failure;
        if (recipe.SourceRecipeId is null)
            return new(RecipeMutationOutcome.ValidationError, Error: "Это не внешний рецепт.");

        return await DeleteTrackedAsync(recipe, revision, cancellationToken);
    }

    /// <summary>
    /// Замена фото: файл сохраняется, ревизия растёт атомарно. При любом неуспехе
    /// записи БД новый файл убирается компенсацией; при гонке чужой файл не
    /// затрагивается, а клиент получает конфликт.
    /// </summary>
    public async Task<RecipeMutationResult> UploadPhotoAsync(
        RecipeTarget target,
        int? revision,
        string extension,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var (recipe, failure) = await FindAsync(
            target, requireOwn: true, includeContent: false, cancellationToken);
        if (failure is not null)
            return failure;

        var stale = RevisionProblem(revision, recipe.Revision);
        if (stale is not null)
            return stale;

        var previous = recipe.PhotoPath;
        return await _photos.ReplaceAsync(
            recipe.Id,
            extension,
            content,
            previous,
            async staged =>
            {
                recipe.PhotoPath = staged;
                recipe.Revision = RecipeRevisionRules.Next(recipe.Revision);
                recipe.UpdatedAt = _clock.GetUtcNow().UtcDateTime;

                try
                {
                    await _db.SaveChangesAsync(cancellationToken);
                    return new PhotoCommit<RecipeMutationResult>(
                        true,
                        new(RecipeMutationOutcome.Ok, Recipe: recipe, Revision: recipe.Revision));
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Правка проиграла гонку: БД не изменилась, новый файл убирается
                    // компенсацией, чужой актуальный файл не трогается.
                    return new PhotoCommit<RecipeMutationResult>(
                        false,
                        new(RecipeMutationOutcome.Conflict,
                            Revision: await _revisions.CurrentAsync(target.Id, cancellationToken)));
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Удаление фото. Если фото нет, это no-op: ревизия не растёт и не делает
    /// ожидаемые версии других клиентов устаревшими.
    /// </summary>
    public async Task<RecipeMutationResult> DeletePhotoAsync(
        RecipeTarget target, int? revision, CancellationToken cancellationToken = default)
    {
        var (recipe, failure) = await FindAsync(
            target, requireOwn: true, includeContent: false, cancellationToken);
        if (failure is not null)
            return failure;

        if (recipe.PhotoPath is null)
            return new(RecipeMutationOutcome.Ok, Recipe: recipe, Revision: recipe.Revision);

        var stale = RevisionProblem(revision, recipe.Revision);
        if (stale is not null)
            return stale;

        var previous = recipe.PhotoPath;
        recipe.PhotoPath = null;
        recipe.Revision = RecipeRevisionRules.Next(recipe.Revision);
        recipe.UpdatedAt = _clock.GetUtcNow().UtcDateTime;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(RecipeMutationOutcome.Conflict, Revision: await _revisions.CurrentAsync(target.Id, cancellationToken));
        }

        _photos.Retire(previous);

        return new(RecipeMutationOutcome.Ok, Recipe: recipe, Revision: recipe.Revision);
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
            return new(RecipeMutationOutcome.Conflict, Revision: await _revisions.CurrentAsync(recipe.Id, cancellationToken));
        }

        _photos.Retire(photoPath);
        return new(RecipeMutationOutcome.Ok, Revision: RecipeRevisionRules.Next(revision.Value));
    }

    /// <summary>
    /// Загрузка рецепта семьи с общим предикатом «есть / свой»: NotFound и
    /// ExternalReadOnly решаются здесь, чтобы не повторять их в каждой операции.
    /// </summary>
    private async Task<(Recipe Recipe, RecipeMutationResult? Failure)> FindAsync(
        RecipeTarget target, bool requireOwn, bool includeContent, CancellationToken cancellationToken)
    {
        var query = _db.Recipes.AsQueryable();
        if (includeContent)
            query = query.Include(r => r.Steps).Include(r => r.Ingredients);

        var recipe = await query.FirstOrDefaultAsync(
            r => r.Id == target.Id && r.FamilyId == target.FamilyId, cancellationToken);
        if (recipe is null)
            return (null!, new(RecipeMutationOutcome.NotFound));
        if (requireOwn && recipe.SourceRecipeId is not null)
            return (recipe, new(RecipeMutationOutcome.ExternalReadOnly));

        return (recipe, null);
    }

    /// <summary>Проверка ожидаемой ревизии: исход ошибки, если она не передана или устарела.</summary>
    private static RecipeMutationResult? RevisionProblem(int? revision, int current)
    {
        if (revision is null)
            return new(RecipeMutationOutcome.MissingRevision, Revision: current);
        if (!RecipeRevisionRules.IsCurrent(revision, current))
            return new(RecipeMutationOutcome.Conflict, Revision: current);
        return null;
    }
}
