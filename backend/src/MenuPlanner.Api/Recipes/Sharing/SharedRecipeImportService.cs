using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Recipes.Sharing;

/// <summary>Исход импорта внешнего рецепта по ссылке на рецепт.</summary>
public enum SharedRecipeImportOutcome
{
    /// <summary>Обёртка создана; <see cref="SharedRecipeImportResult.RecipeId"/> — её id.</summary>
    Created,

    /// <summary>Ссылка неизвестна, отозвана или источник удалён.</summary>
    InvalidLink,

    /// <summary>Ссылка ведёт на рецепт семьи получателя — добавлять себе нечего.</summary>
    OwnFamily,

    /// <summary>Внешний рецепт на этот источник уже есть; id существующей обёртки.</summary>
    AlreadyImported
}

/// <summary>
/// Итог импорта: исход и, для <see cref="SharedRecipeImportOutcome.Created"/> и
/// <see cref="SharedRecipeImportOutcome.AlreadyImported"/>, id рецепта-обёртки.
/// </summary>
public sealed record SharedRecipeImportResult(
    SharedRecipeImportOutcome Outcome,
    Guid RecipeId = default);

/// <summary>
/// Импорт чужого рецепта себе по ссылке: проверка ссылки и источника, запрет
/// импорта рецепта своей же семьи и создание строки-обёртки, читающей источник
/// живьём. Гонка параллельного импорта разрешается уникальным индексом
/// <c>(FamilyId, SourceRecipeId)</c>: проигравший перечитывает победителя и
/// отдаёт его id, а не дубль. Scoped-сервис, читает и пишет БД.
/// </summary>
public sealed class SharedRecipeImportService
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    public SharedRecipeImportService(AppDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<SharedRecipeImportResult> ImportAsync(
        string token, Guid familyId, CancellationToken cancellationToken = default)
    {
        var share = await _db.RecipeShares
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Token == token, cancellationToken);
        if (share is null || share.RevokedAt is not null)
            return new(SharedRecipeImportOutcome.InvalidLink);

        var source = await _db.Recipes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == share.RecipeId, cancellationToken);
        if (source is null)
            return new(SharedRecipeImportOutcome.InvalidLink);

        if (source.FamilyId == familyId)
            return new(SharedRecipeImportOutcome.OwnFamily);

        var existing = await _db.Recipes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.FamilyId == familyId && r.SourceRecipeId == source.Id, cancellationToken);
        if (existing is not null)
            return new(SharedRecipeImportOutcome.AlreadyImported, existing.Id);

        var now = _clock.GetUtcNow().UtcDateTime;
        var externalRecipe = new Recipe
        {
            FamilyId = familyId,
            Name = source.Name,
            SourceRecipeId = source.Id,
            SourceFamilyId = source.FamilyId,
            SourceToken = token,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Recipes.Add(externalRecipe);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Гонка: параллельный импорт успел вставить внешний рецепт раньше.
            // Уникальный индекс (FamilyId, SourceRecipeId) не даёт создать дубль —
            // отдаём уже существующий, как и в проверке выше.
            _db.Entry(externalRecipe).State = EntityState.Detached;

            var concurrent = await _db.Recipes
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.FamilyId == familyId && r.SourceRecipeId == source.Id, cancellationToken);
            if (concurrent is not null)
                return new(SharedRecipeImportOutcome.AlreadyImported, concurrent.Id);

            throw;
        }

        return new(SharedRecipeImportOutcome.Created, externalRecipe.Id);
    }
}
