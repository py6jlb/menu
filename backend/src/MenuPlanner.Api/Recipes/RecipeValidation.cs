using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Recipes;

/// <summary>
/// Ошибка ввода рецепта: машинный код, русский текст и путь поля формы. Поле —
/// лучшая привязка для клиента (`name`, `ingredients[2].amount`); null, когда
/// ошибка относится к запросу целиком.
/// </summary>
public sealed record RecipeFieldError(string Code, string Message, string? Field = null);

/// <summary>
/// Чистые правила рецепта: проверка запроса на подбор и на сохранение, а также
/// перенос запроса в доменную сущность. Без БД и HTTP — юнит-тестируется напрямую.
/// Значения проверяются относительно реальных ограничений хранения, поэтому
/// принятое значение не округляется и не отклоняется базой неожиданно.
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

        foreach (var season in filters.Seasons ?? new List<string>())
        {
            if (season is null)
                return "Недопустимое значение сезона.";
            var normalized = season.Trim().ToLowerInvariant();
            if (!RecipeCatalog.Seasons.Contains(normalized))
                return $"Недопустимое значение сезона: «{season}».";
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
        recipe.Seasonality = NormalizeSeasons(request.Seasonality);
        recipe.Diet = DietCatalog.NormalizeAll(request.Diet);
        recipe.Steps = MapSteps(request);
        recipe.Ingredients = MapIngredients(request);
    }

    public static List<RecipeStep> MapSteps(RecipeRequest request)
    {
        var texts = NonNullElements(request.Steps)
            .Select(s => s.Text?.Trim())
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

        foreach (var ing in NonNullElements(request.Ingredients))
        {
            if (IsBlankIngredient(ing))
                continue;

            var name = ing.Name?.Trim();
            var unit = ing.Unit?.Trim();
            var note = ing.Note?.Trim();

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

    public static List<string> NormalizeStrings(List<string>? source)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in source ?? new List<string>())
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var trimmed = value.Trim();
            if (seen.Add(trimmed))
                result.Add(trimmed);
        }

        return result;
    }

    public static RecipeFieldError? Validate(RecipeRequest request)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            return new("name_required", "Укажите название рецепта.", "name");
        if (name.Length > RecipeCatalog.NameMaxLength)
            return new(
                "name_too_long",
                $"Название рецепта не должно превышать {RecipeCatalog.NameMaxLength} символов.",
                "name");

        if (request.Description?.Trim().Length > RecipeCatalog.TextMaxLength)
            return new(
                "description_too_long",
                $"Описание не должно превышать {RecipeCatalog.TextMaxLength} символов.",
                "description");

        if (request.CookTimeMinutes is not (>= RecipeCatalog.CookTimeMin and <= RecipeCatalog.CookTimeMax))
            return new(
                "cook_time_range",
                $"Укажите время приготовления (мин) от {RecipeCatalog.CookTimeMin} до {RecipeCatalog.CookTimeMax}.",
                "cookTimeMinutes");

        if (request.Servings is not (>= RecipeCatalog.ServingsMin and <= RecipeCatalog.ServingsMax))
            return new(
                "servings_range",
                $"Укажите количество порций от {RecipeCatalog.ServingsMin} до {RecipeCatalog.ServingsMax}.",
                "servings");

        if (request.Difficulty is not (>= RecipeCatalog.DifficultyMin and <= RecipeCatalog.DifficultyMax))
            return new(
                "difficulty_range",
                $"Укажите сложность от {RecipeCatalog.DifficultyMin} до {RecipeCatalog.DifficultyMax}.",
                "difficulty");

        if (request.Calories is < 0 or > RecipeCatalog.CaloriesMax)
            return new(
                "calories_range",
                $"Калорийность должна быть в диапазоне от 0 до {RecipeCatalog.CaloriesMax}.",
                "calories");

        var steps = NonNullElements(request.Steps).ToList();
        if (steps.Count > RecipeCatalog.StepsMax)
            return new(
                "steps_too_many",
                $"Слишком много шагов (максимум {RecipeCatalog.StepsMax}).",
                "steps");

        var stepTexts = steps
            .Select(s => s.Text?.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();
        if (stepTexts.Count == 0)
            return new("steps_required", "Добавьте хотя бы один шаг приготовления.", "steps");
        if (stepTexts.Any(s => s!.Length > RecipeCatalog.TextMaxLength))
            return new(
                "step_too_long",
                $"Текст шага не должен превышать {RecipeCatalog.TextMaxLength} символов.",
                "steps");

        var tags = NormalizeStrings(request.Tags);
        if (tags.Count > RecipeCatalog.TagsMax)
            return new(
                "tags_too_many",
                $"Слишком много тегов (максимум {RecipeCatalog.TagsMax}).",
                "tags");
        if (tags.Any(t => t.Length > RecipeCatalog.TagMaxLength))
            return new(
                "tag_too_long",
                $"Теги не должны превышать {RecipeCatalog.TagMaxLength} символов.",
                "tags");

        var seasons = NormalizeSeasons(request.Seasonality);
        var invalidSeason = seasons.FirstOrDefault(s => !RecipeCatalog.Seasons.Contains(s));
        if (invalidSeason is not null)
            return new(
                "season_invalid",
                $"Недопустимое значение сезона: «{invalidSeason}».",
                "seasonality");

        var diet = DietCatalog.NormalizeAll(request.Diet);
        if (diet.Count > RecipeCatalog.DietsMax)
            return new(
                "diets_too_many",
                $"Слишком много меток диеты (максимум {RecipeCatalog.DietsMax}).",
                "diet");
        if (diet.Any(d => d.Length > RecipeCatalog.DietMaxLength))
            return new(
                "diet_too_long",
                $"Метки диеты не должны превышать {RecipeCatalog.DietMaxLength} символов.",
                "diet");

        var ingredients = NonNullElements(request.Ingredients).ToList();
        if (ingredients.Count > RecipeCatalog.IngredientsMax)
            return new(
                "ingredients_too_many",
                $"Слишком много ингредиентов (максимум {RecipeCatalog.IngredientsMax}).",
                "ingredients");

        for (var index = 0; index < ingredients.Count; index++)
        {
            var ing = ingredients[index];
            if (IsBlankIngredient(ing))
                continue;

            var ingredientName = ing.Name?.Trim();
            var unit = ing.Unit?.Trim();
            var note = ing.Note?.Trim();
            var field = $"ingredients[{index}]";

            if (string.IsNullOrEmpty(ingredientName))
                return new("ingredient_name_required", "Укажите название ингредиента.", $"{field}.name");
            if (ingredientName.Length > RecipeCatalog.IngredientNameMaxLength)
                return new(
                    "ingredient_name_too_long",
                    $"Название ингредиента не должно превышать {RecipeCatalog.IngredientNameMaxLength} символов.",
                    $"{field}.name");

            var amountError = ValidateAmount(ing.Amount, ingredientName, field);
            if (amountError is not null)
                return amountError;

            if (string.IsNullOrEmpty(unit))
                return new("ingredient_unit_required", $"Укажите единицу измерения ингредиента «{ingredientName}».", $"{field}.unit");
            if (unit.Length > RecipeCatalog.UnitMaxLength || !RecipeCatalog.Units.Contains(unit))
                return new("ingredient_unit_invalid", $"Недопустимая единица измерения «{unit}».", $"{field}.unit");
            if (note?.Length > RecipeCatalog.NoteMaxLength)
                return new(
                    "ingredient_note_too_long",
                    $"Примечание к ингредиенту не должно превышать {RecipeCatalog.NoteMaxLength} символов.",
                    $"{field}.note");
        }

        return null;
    }

    /// <summary>
    /// Количество согласовано с decimal-хранилищем: положительное, в пределах
    /// numeric(10,2) и не точнее сотых. Хвостовые нули большей точностью не
    /// считаются — сравнение десятичных ignores scale.
    /// </summary>
    private static RecipeFieldError? ValidateAmount(decimal? amount, string ingredientName, string field)
    {
        if (amount is null)
            return new("ingredient_amount_required", $"Укажите количество ингредиента «{ingredientName}».", $"{field}.amount");

        if (amount <= 0)
            return new(
                "ingredient_amount_positive",
                $"Количество ингредиента «{ingredientName}» должно быть положительным.",
                $"{field}.amount");

        if (decimal.Round(amount.Value, RecipeCatalog.AmountScale) != amount.Value)
            return new(
                "ingredient_amount_precision",
                $"Количество ингредиента «{ingredientName}» должно иметь не больше двух знаков после запятой.",
                $"{field}.amount");

        if (amount > RecipeCatalog.AmountMax)
            return new(
                "ingredient_amount_range",
                $"Количество ингредиента «{ingredientName}» не должно превышать {RecipeCatalog.AmountMax}.",
                $"{field}.amount");

        return null;
    }

    /// <summary>
    /// Элементы коллекции без null: malformed payload с null вместо объекта должен
    /// давать контролируемый отказ, а не исключение обращения к null.
    /// </summary>
    private static IEnumerable<T> NonNullElements<T>(IEnumerable<T?>? source) where T : class =>
        (source ?? Enumerable.Empty<T?>()).Where(element => element is not null).Select(element => element!);

    /// <summary>Ингредиент без единого значимого поля — пустая строка формы, не ошибка.</summary>
    private static bool IsBlankIngredient(RecipeIngredientRequest? ingredient) =>
        ingredient is null
        || (string.IsNullOrEmpty(ingredient.Name?.Trim())
            && ingredient.Amount is null
            && string.IsNullOrEmpty(ingredient.Unit?.Trim())
            && string.IsNullOrEmpty(ingredient.Note?.Trim()));

    /// <summary>Сезоны — нормализованные строки в нижнем регистре.</summary>
    private static List<string> NormalizeSeasons(List<string>? source) =>
        NormalizeStrings(source).Select(s => s.ToLowerInvariant()).ToList();
}
