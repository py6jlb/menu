namespace MenuPlanner.Api.ShoppingList;

public sealed record ShoppingListItemDto(
    string Name,
    decimal Amount,
    string Unit,
    string Display);

public sealed record ShoppingListDto(
    string WeekStart,
    IReadOnlyList<ShoppingListItemDto> Items);

public sealed record ShoppingListErrorDto(string Error);