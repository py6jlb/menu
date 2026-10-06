using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Recipes.External;

/// <summary>
/// Чистая проекция живого контента внешнего рецепта на источник. Внешний рецепт хранит
/// только кэш имени; остальное (ингредиенты, порции, параметры, шаги) живёт у семьи-источника
/// (ADR-0001). Загрузка источников из БД — в scoped <see cref="ExternalRecipeSourceLoader"/>.
/// Сломанный источник сюда не попадает: вызывающий сам решает, показать кэш имени или ничего.
/// </summary>
public static class ExternalRecipeContentResolver
{
    /// <summary>
    /// Проецирует внешний рецепт на живой источник: id и связь остаются от внешнего рецепта
    /// (на них ссылаются план и покупки), контент берётся из источника. Без источника возвращает
    /// сам внешний рецепт с кэшированным именем и пустым контентом.
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
    /// Возвращает живой источник для внешнего рецепта либо сам внешний рецепт, если источника больше нет.
    /// </summary>
    public static Recipe Resolve(
        Recipe wrapper, IReadOnlyDictionary<Guid, Recipe> liveSources) =>
        wrapper.SourceRecipeId is Guid sourceId && liveSources.TryGetValue(sourceId, out var source)
            ? Materialize(wrapper, source)
            : wrapper;
}
