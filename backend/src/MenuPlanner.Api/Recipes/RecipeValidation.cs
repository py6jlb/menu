using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Recipes;

/// <summary>
/// Чистые правила рецепта: проверка запроса на подбор и на сохранение, а также
/// перенос запроса в доменную сущность. Без БД и HTTP — юнит-тестируется напрямую.
/// </summary>
public static class RecipeValidation
{
    public static string? ValidateMatch(RecipeMatchRequest request)
    {
        var filters = request.Filters;
        if (filters is null)
            return null;

        if (filters.MaxDifficulty is { } maxDifficulty &&
            maxDifficulty is < RecipeCatalog.DifficultyMin or > RecipeCatalog.DifficultyMax)
            return $"Максимальная сложность должна быть от {RecipeCatalog.DifficultyMin} до {RecipeCatalog.DifficultyMax}.";

        if (filters.MaxCalories is { } maxCalories && maxCalories < 0)
            return "Максимальная калорийность не может быть отрицательной.";

        if (filters.MaxCookTimeMinutes is { } maxCookTime && maxCookTime < 0)
            return "Максимальное время приготовления не может быть отрицательным.";

        if (filters.Seasons is { Count: > 0 })
        {
            foreach (var season in filters.Seasons)
            {
                var normalized = season.Trim().ToLowerInvariant();
                if (!RecipeCatalog.Seasons.Contains(normalized))
                    return $"Недопустимое значение сезона: «{season}».";
            }
        }

        return null;
    }

    public static void Apply(Recipe recipe, RecipeRequest request)
    {
        recipe.Name = request.Name!.Trim();
        recipe.Description = string.IsNullOrWhiteSpace(request.Description)
            ? null
            : request.Description.Trim();
        recipe.CookTimeMinutes = request.CookTimeMinutes!.Value;
        recipe.Servings = request.Servings!.Value;
        recipe.Difficulty = request.Difficulty!.Value;
        recipe.Calories = request.Calories;
        recipe.Tags = NormalizeStrings(request.Tags);
        recipe.Seasonality = NormalizeStrings(request.Seasonality)
            .Select(s => s.ToLowerInvariant())
            .ToList();
        recipe.Diet = NormalizeStrings(request.Diet);
        recipe.Steps = MapSteps(request);
        recipe.Ingredients = MapIngredients(request);
    }

    public static List<RecipeStep> MapSteps(RecipeRequest request)
    {
        var texts = (request.Steps ?? new List<RecipeStepRequest>())
            .Select(s => s?.Text?.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .Select(t => t!)
            .ToList();

        return texts
            .Select((text, index) => new RecipeStep { Order = index, Text = text })
            .ToList();
    }

    public static List<RecipeIngredient> MapIngredients(RecipeRequest request)
    {
        var result = new List<RecipeIngredient>();
        var order = 0;

        foreach (var ing in request.Ingredients ?? new List<RecipeIngredientRequest>())
        {
            var name = ing.Name?.Trim();
            var unit = ing.Unit?.Trim();
            var note = ing.Note?.Trim();

            if (string.IsNullOrEmpty(name) && ing.Amount is null && string.IsNullOrEmpty(unit) && string.IsNullOrEmpty(note))
                continue;

            result.Add(new RecipeIngredient
            {
                Order = order++,
                Name = name!,
                Amount = ing.Amount!.Value,
                Unit = unit!,
                Note = string.IsNullOrEmpty(note) ? null : note
            });
        }

        return result;
    }

    public static List<string> NormalizeStrings(List<string>? source) =>
        (source ?? new List<string>())
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static string? Validate(RecipeRequest request)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            return "Укажите название рецепта.";
        if (name.Length > RecipeCatalog.NameMaxLength)
            return $"Название рецепта не должно превышать {RecipeCatalog.NameMaxLength} символов.";

        if (request.Description?.Trim().Length > RecipeCatalog.TextMaxLength)
            return $"Описание не должно превышать {RecipeCatalog.TextMaxLength} символов.";

        if (request.CookTimeMinutes is not (>= RecipeCatalog.CookTimeMin and <= RecipeCatalog.CookTimeMax))
            return $"Укажите время приготовления (мин) от {RecipeCatalog.CookTimeMin} до {RecipeCatalog.CookTimeMax}.";

        if (request.Servings is not (>= RecipeCatalog.ServingsMin and <= RecipeCatalog.ServingsMax))
            return $"Укажите количество порций от {RecipeCatalog.ServingsMin} до {RecipeCatalog.ServingsMax}.";

        if (request.Difficulty is not (>= RecipeCatalog.DifficultyMin and <= RecipeCatalog.DifficultyMax))
            return $"Укажите сложность от {RecipeCatalog.DifficultyMin} до {RecipeCatalog.DifficultyMax}.";

        if (request.Calories is < 0 or > RecipeCatalog.CaloriesMax)
            return $"Калорийность должна быть в диапазоне от 0 до {RecipeCatalog.CaloriesMax}.";

        var steps = (request.Steps ?? new List<RecipeStepRequest>())
            .Select(s => s?.Text?.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();
        if (steps.Count == 0)
            return "Добавьте хотя бы один шаг приготовления.";
        if (steps.Any(s => s!.Length > RecipeCatalog.TextMaxLength))
            return $"Текст шага не должен превышать {RecipeCatalog.TextMaxLength} символов.";

        var tags = NormalizeStrings(request.Tags);
        if (tags.Any(t => t.Length > RecipeCatalog.TagMaxLength))
            return $"Теги не должны превышать {RecipeCatalog.TagMaxLength} символов.";
        if (tags.Count > 100)
            return "Слишком много тегов (максимум 100).";

        var seasons = NormalizeStrings(request.Seasonality)
            .Select(s => s.ToLowerInvariant())
            .ToList();
        var invalidSeason = seasons.FirstOrDefault(s => !RecipeCatalog.Seasons.Contains(s));
        if (invalidSeason is not null)
            return $"Недопустимое значение сезона: «{invalidSeason}».";

        var diet = NormalizeStrings(request.Diet);
        if (diet.Any(d => d.Length > RecipeCatalog.DietMaxLength))
            return $"Метки диеты не должны превышать {RecipeCatalog.DietMaxLength} символов.";

        foreach (var ing in request.Ingredients ?? new List<RecipeIngredientRequest>())
        {
            var ingredientName = ing.Name?.Trim();
            var unit = ing.Unit?.Trim();
            var note = ing.Note?.Trim();

            if (string.IsNullOrEmpty(ingredientName) && ing.Amount is null
                && string.IsNullOrEmpty(unit) && string.IsNullOrEmpty(note))
                continue;

            if (string.IsNullOrEmpty(ingredientName))
                return "Укажите название ингредиента.";
            if (ingredientName.Length > RecipeCatalog.IngredientNameMaxLength)
                return $"Название ингредиента не должно превышать {RecipeCatalog.IngredientNameMaxLength} символов.";
            if (ing.Amount is not (> 0))
                return $"Укажите количество ингредиента «{ingredientName}».";
            if (string.IsNullOrEmpty(unit))
                return $"Укажите единицу измерения ингредиента «{ingredientName}».";
            if (!RecipeCatalog.Units.Contains(unit))
                return $"Недопустимая единица измерения «{unit}».";
            if (note?.Length > RecipeCatalog.NoteMaxLength)
                return $"Примечание к ингредиенту не должно превышать {RecipeCatalog.NoteMaxLength} символов.";
        }

        return null;
    }
}
