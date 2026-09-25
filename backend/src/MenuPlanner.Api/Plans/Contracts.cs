namespace MenuPlanner.Api.Plans;

public sealed record PlanEntryRequest(int Day, string MealType, Guid RecipeId, int Portions);

public sealed record SaveWeekPlanRequest(IReadOnlyList<PlanEntryRequest>? Entries);

public sealed record PlanEntryDto(
    int Day,
    string MealType,
    Guid RecipeId,
    string RecipeName,
    int Portions,
    string? State = null);

public sealed record WeekPlanDto(string WeekStart, IReadOnlyList<PlanEntryDto> Entries);

public sealed record PlanErrorDto(string Error);
