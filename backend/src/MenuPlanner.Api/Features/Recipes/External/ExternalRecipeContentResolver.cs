using MenuPlanner.Domain.Recipes;

namespace MenuPlanner.Api.Features.Recipes.External;

/// <summary>
/// Чистая проекция живого контента внешнего рецепта на источник. Внешний рецепт хранит
/// только кэш имени; остальное (ингредиенты, порции, параметры, шаги) живёт у семьи-источника
/// (ADR-0001). Загрузка источников из БД — в scoped <see cref="ExternalRecipeSourceLoader"/>.
/// Сломанный источник сюда не попадает: вызывающий сам решает, показать кэш имени или ничего.
/// </summary>
public static class ExternalRecipeContentResolver
{
    /// <summary>
    /// Переносит контент живого источника в целевую строку: текстовые поля, параметры
    /// и дочерние коллекции (шаги, ингредиенты). Общий шаг материализации при чтении
    /// и промоушена в копию — набор полей источника больше не дублируется. Файлы фото
    /// и документа сюда не входят: при чтении они берутся у источника, при промоушене
    /// подменяются скопированными.
    /// </summary>
    public static void CopySourceContent(Recipe target, Recipe source)
    {
        target.Name = source.Name;
        target.Description = source.Description;
        target.CookTimeMinutes = source.CookTimeMinutes;
        target.Servings = source.Servings;
        target.Difficulty = source.Difficulty;
        target.Calories = source.Calories;
        target.Tags = new List<string>(source.Tags);
        target.Seasonality = new List<string>(source.Seasonality);
        target.Diet = DietCatalog.NormalizeAll(source.Diet);
        target.PhotoPath = source.PhotoPath;
        target.DocumentPath = source.DocumentPath;
        target.Steps = source.Steps
            .OrderBy(s => s.Order)
            .Select(s => new RecipeStep { Order = s.Order, Text = s.Text })
            .ToList();
        target.Ingredients = source.Ingredients
            .OrderBy(i => i.Order)
            .Select(i => new RecipeIngredient
            {
                Order = i.Order,
                Name = i.Name,
                Amount = i.Amount,
                Unit = i.Unit,
                Note = i.Note,
                Category = i.Category
            })
            .ToList();
    }

    /// <summary>
    /// Проецирует внешний рецепт на живой источник: id и связь остаются от внешнего рецепта
    /// (на них ссылаются план и покупки), контент и даты правки/создания берутся из источника.
    /// Без источника возвращает сам внешний рецепт с кэшированным именем.
    /// </summary>
    public static Recipe Materialize(Recipe wrapper, Recipe? source)
    {
        if (source is null)
            return wrapper;

        var effective = new Recipe
        {
            Id = wrapper.Id,
            FamilyId = wrapper.FamilyId,
            Name = source.Name,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt,
            Revision = wrapper.Revision,
            CopiedFromFamilyName = wrapper.CopiedFromFamilyName,
            SourceRecipeId = wrapper.SourceRecipeId,
            SourceFamilyId = wrapper.SourceFamilyId,
            SourceToken = wrapper.SourceToken
        };
        CopySourceContent(effective, source);
        return effective;
    }

    /// <summary>
    /// Проекция краткого списка: метаданные берутся у живого источника, если он есть,
    /// иначе — из кэша самой обёртки (слияние вида live?.X ?? stored.X). Дочерние
    /// коллекции не читаются: краткому списку они не нужны.
    /// </summary>
    public static Recipe Project(Recipe wrapper, RecipeSourceSummary? source) => new()
    {
        Id = wrapper.Id,
        FamilyId = wrapper.FamilyId,
        Name = source?.Name ?? wrapper.Name,
        Difficulty = source?.Difficulty ?? wrapper.Difficulty,
        Calories = source is null ? wrapper.Calories : source.Calories,
        CookTimeMinutes = source?.CookTimeMinutes ?? wrapper.CookTimeMinutes,
        Servings = source?.Servings ?? wrapper.Servings,
        Tags = source?.Tags ?? wrapper.Tags,
        Seasonality = source?.Seasonality ?? wrapper.Seasonality,
        Diet = source?.Diet ?? wrapper.Diet,
        PhotoPath = source is null ? wrapper.PhotoPath : source.PhotoPath,
        CopiedFromFamilyName = wrapper.CopiedFromFamilyName,
        CreatedAt = wrapper.CreatedAt,
        UpdatedAt = wrapper.UpdatedAt,
        Revision = wrapper.Revision,
        SourceRecipeId = wrapper.SourceRecipeId,
        SourceFamilyId = wrapper.SourceFamilyId,
        SourceToken = wrapper.SourceToken
    };
}
