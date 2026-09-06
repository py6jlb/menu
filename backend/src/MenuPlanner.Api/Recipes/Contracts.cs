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
    List<RecipeIngredientRequest>? Ingredients);

public sealed record RecipeSummaryDto(
    Guid Id,
    string Name,
    int Difficulty,
    int? Calories,
    int CookTimeMinutes,
    int Servings,
    IReadOnlyList<string> Tags,
<<<<<<< HEAD
    int RepetitionCount = 0);
=======
    string? PhotoUrl);
>>>>>>> ticket/10-photos

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
<<<<<<< HEAD
    int RepetitionCount = 0);

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
=======
    string? PhotoUrl);
>>>>>>> ticket/10-photos

public sealed record RecipeErrorDto(string Error);