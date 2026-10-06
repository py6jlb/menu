namespace MenuPlanner.Api.Plans;

public sealed record PlanEntryRequest(int Day, string MealType, Guid RecipeId, int Portions);

/// <summary>
/// Полная замена недели. <see cref="ExpectedRevision"/> — ревизия, которую
/// клиент прочитал; <see cref="WeekPlanRevisions.Initial"/> означает создание.
/// </summary>
public sealed record SaveWeekPlanRequest(
    IReadOnlyList<PlanEntryRequest>? Entries,
    int ExpectedRevision = WeekPlanRevisions.Initial);

public sealed record PlanEntryDto(
    int Day,
    string MealType,
    Guid RecipeId,
    string RecipeName,
    int Portions,
    string? State = null);

public sealed record WeekPlanDto(
    string WeekStart,
    int Revision,
    IReadOnlyList<PlanEntryDto> Entries);

/// <summary>Конфликт ревизий: текущая серверная версия недели для сравнения.</summary>
public sealed record PlanConflictDto(
    string Error,
    string WeekStart,
    int Revision,
    IReadOnlyList<PlanEntryDto> Entries);

/// <summary>
/// Ошибка ввода плана: русское объяснение, машинный код и путь поля (например,
/// `entries[2].portions`) для привязки причины к конкретной записи.
/// </summary>
public sealed record PlanErrorDto(string Error, string? Code = null, string? Field = null);
