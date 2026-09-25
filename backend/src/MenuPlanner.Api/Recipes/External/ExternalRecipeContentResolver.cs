using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Recipes.External;

/// <summary>
/// Читает живой контент внешних рецептов из источника. Обёртка хранит только кэш имени;
/// остальное (ингредиенты, порции, параметры, шаги) живёт у семьи-источника (ADR-0001).
/// Сломанный источник сюда не попадает: вызывающий сам решает, показать кэш имени или ничего.
/// </summary>
public static class ExternalRecipeContentResolver
{
    /// <summary>
    /// Загружает источники по их id вместе с ингредиентами и шагами, без отслеживания.
    /// Возвращает карту «id источника → рецепт-источник»; отсутствующие в БД просто не попадают.
    /// </summary>
    public static async Task<Dictionary<Guid, Recipe>> LoadSourcesAsync(
        AppDbContext db, IEnumerable<Guid> sourceRecipeIds)
    {
        var ids = sourceRecipeIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, Recipe>();

        var sources = await db.Recipes
            .AsNoTracking()
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .Where(r => ids.Contains(r.Id))
            .ToListAsync();

        return sources.ToDictionary(s => s.Id);
    }

    /// <summary>
    /// Проецирует обёртку на живой источник: id и связь остаются от обёртки (на них ссылаются
    /// план и покупки), контент берётся из источника. Без источника возвращает саму обёртку
    /// с кэшированным именем и пустым контентом.
    /// </summary>
    public static Recipe Materialize(Recipe wrapper, Recipe? source)
    {
        if (source is null)
            return wrapper;

        return new Recipe
        {
            Id = wrapper.Id,
            FamilyId = wrapper.FamilyId,
            Name = source.Name,
            Description = source.Description,
            PhotoPath = source.PhotoPath,
            CookTimeMinutes = source.CookTimeMinutes,
            Servings = source.Servings,
            Difficulty = source.Difficulty,
            Calories = source.Calories,
            Tags = source.Tags,
            Seasonality = source.Seasonality,
            Diet = source.Diet,
            CreatedAt = wrapper.CreatedAt,
            UpdatedAt = source.UpdatedAt,
            SourceRecipeId = wrapper.SourceRecipeId,
            SourceFamilyId = wrapper.SourceFamilyId,
            SourceToken = wrapper.SourceToken,
            Ingredients = source.Ingredients,
            Steps = source.Steps
        };
    }

    /// <summary>
    /// Возвращает живой источник для обёртки либо саму обёртку, если источника больше нет.
    /// </summary>
    public static Recipe Resolve(
        Recipe wrapper, IReadOnlyDictionary<Guid, Recipe> liveSources) =>
        wrapper.SourceRecipeId is Guid sourceId && liveSources.TryGetValue(sourceId, out var source)
            ? Materialize(wrapper, source)
            : wrapper;
}
