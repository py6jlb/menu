namespace MenuPlanner.Api.ShoppingList;

public sealed record ShoppingListItemDto(
    string Name,
    decimal Amount,
    string Unit,
    string Display,
    string? Category = null);

/// <summary>
/// Запись плана, которую нельзя посчитать в списке покупок: где стоит и почему исключена.
/// </summary>
public sealed record ShoppingListExcludedDto(
    int Day,
    string MealType,
    Guid RecipeId,
    string RecipeName,
    string Reason);

/// <summary>
/// Список покупок недели. <see cref="HasPlan"/> отличает отсутствующий план от пустого;
/// <see cref="Excluded"/> возвращает записи, выпавшие из расчёта, чтобы у пользователя
/// не создавалось ощущение полного списка.
/// </summary>
public sealed record ShoppingListDto(
    string WeekStart,
    bool HasPlan,
    IReadOnlyList<ShoppingListItemDto> Items,
    IReadOnlyList<ShoppingListExcludedDto> Excluded);

public sealed record ShoppingListErrorDto(string Error);
