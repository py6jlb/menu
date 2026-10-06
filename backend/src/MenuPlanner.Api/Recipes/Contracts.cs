namespace MenuPlanner.Api.Recipes;

public sealed record RecipeStepRequest(string? Text);

public sealed record RecipeIngredientRequest(string? Name, decimal? Amount, string? Unit, string? Note);

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

public sealed record RecipeIngredientDto(
    Guid Id,
    string Name,
    decimal Amount,
    string Unit,
    string? Note);

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
    int Revision = 1);

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
    int MatchScore);

public sealed record RecipeMatchResponse(IReadOnlyList<RecipeMatchItemDto> Items);

public sealed record RecipeErrorDto(string Error);

/// <summary>
/// Конфликт ревизий: сохранение основано на устаревшей версии. <see cref="Revision"/>
/// — актуальная серверная ревизия, чтобы UI мог сравнить, не перезаписывая черновик.
/// </summary>
public sealed record RecipeConflictDto(string Error, int Revision = 0);

public sealed record RecipeImportResultDto(Guid RecipeId);

public sealed record RecipeImportConflictDto(string Error, Guid RecipeId);

public sealed record RecipeShareDto(
    Guid RecipeId,
    string Token,
    string Url,
    string Path,
    DateTime CreatedAt,
    bool Revoked,
    DateTime? RevokedAt);