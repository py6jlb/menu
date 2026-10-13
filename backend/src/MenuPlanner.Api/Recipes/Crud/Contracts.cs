namespace MenuPlanner.Api.Recipes.Crud;

/// <summary>Шаг рецепта во входящем запросе.</summary>
public sealed record RecipeStepRequest(string? Text);

/// <summary>Ингредиент рецепта во входящем запросе.</summary>
public sealed record RecipeIngredientRequest(
    string? Name, decimal? Amount, string? Unit, string? Note, string? Category = null);

/// <summary>Создание/правка рецепта: поля, шаги, ингредиенты и ожидаемая ревизия.</summary>
public sealed record RecipeRequest(
    string? Name,
    string? Description,
    int? CookTimeMinutes,
    int? Servings,
    int? Difficulty,
    int? Calories,
    List<string>? Tags,
    List<string>? Seasonality,
    List<string>? Diet,
    List<RecipeStepRequest>? Steps,
    List<RecipeIngredientRequest>? Ingredients,
    // Ожидаемая ревизия при редактировании. Чтение отдаёт её в RecipeDto;
    // изменение с устаревшей ревизией не выполняется. Для создания не нужна.
    int? Revision = null);

/// <summary>Краткая карточка рецепта для списка: ключевые поля, повторяемость и внешнее состояние.</summary>
public sealed record RecipeSummaryDto(
    Guid Id,
    string Name,
    int Difficulty,
    int? Calories,
    int CookTimeMinutes,
    int Servings,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Seasonality,
    IReadOnlyList<string> Diet,
    int RepetitionCount = 0,
    string? PhotoUrl = null,
    bool IsExternal = false,
    string? SourceFamilyName = null,
    string? State = null,
    string? CopiedFromFamilyName = null);

/// <summary>Ингредиент рецепта в ответе.</summary>
public sealed record RecipeIngredientDto(
    Guid Id,
    string Name,
    decimal Amount,
    string Unit,
    string? Note,
    string? Category = null);

/// <summary>Полная карточка рецепта в ответе.</summary>
public sealed record RecipeDto(
    Guid Id,
    string Name,
    string? Description,
    IReadOnlyList<string> Steps,
    int CookTimeMinutes,
    int Servings,
    int Difficulty,
    int? Calories,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Seasonality,
    IReadOnlyList<string> Diet,
    IReadOnlyList<RecipeIngredientDto> Ingredients,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int RepetitionCount = 0,
    string? PhotoUrl = null,
    bool IsExternal = false,
    string? SourceFamilyName = null,
    Guid? SourceFamilyId = null,
    string? State = null,
    string? CopiedFromFamilyName = null,
    int Revision = 1,
    string? DocumentUrl = null);

/// <summary>
/// Кандидат подбора. <c>RepetitionCount</c> — число различных недель окна (до
/// выбранной включительно), где блюдо стоит в сохранённом плане; повторения
/// внутри одной недели дают один вклад.
/// </summary>
public sealed record RecipeMatchItemDto(
    Guid RecipeId,
    string Name,
    int Difficulty,
    int? Calories,
    int CookTimeMinutes,
    int Servings,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Seasonality,
    IReadOnlyList<string> Diet,
    string? PhotoUrl,
    int MatchScore,
    bool IsExternal = false,
    string? SourceFamilyName = null,
    string? State = null,
    int RepetitionCount = 0);

/// <summary>Ответ подбора рецептов: элементы и окно повторяемости.</summary>
public sealed record RecipeMatchResponse(
    IReadOnlyList<RecipeMatchItemDto> Items,
    int RepetitionWindowWeeks = 0);

/// <summary>Простой текстовый ответ об ошибке рецепта.</summary>
public sealed record RecipeErrorDto(string Error);

/// <summary>
/// Ошибка ввода рецепта: русское объяснение, машинный код и путь поля формы для
/// привязки причины к конкретному вводу.
/// </summary>
public sealed record RecipeValidationErrorDto(string Error, string Code, string? Field = null);

/// <summary>
/// Конфликт ревизий: сохранение основано на устаревшей версии. <see cref="Revision"/>
/// — актуальная серверная ревизия, чтобы UI мог сравнить, не перезаписывая черновик.
/// </summary>
public sealed record RecipeConflictDto(string Error, int Revision = 0);

/// <summary>Результат импорта общего рецепта: id созданного рецепта.</summary>
public sealed record RecipeImportResultDto(Guid RecipeId);

/// <summary>Конфликт импорта: рецепт уже добавлен в семью.</summary>
public sealed record RecipeImportConflictDto(string Error, Guid RecipeId);

/// <summary>Состояние публичной ссылки на рецепт: токен, URL и признак отзыва.</summary>
public sealed record RecipeShareDto(
    Guid RecipeId,
    string Token,
    string Url,
    string Path,
    DateTime CreatedAt,
    bool Revoked,
    DateTime? RevokedAt);