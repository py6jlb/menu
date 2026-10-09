namespace MenuPlanner.Api.Ingredients;

/// <summary>Подсказка названия ингредиента с категорией продукта (или null для «Прочего»).</summary>
public sealed record IngredientSuggestionDto(string Name, string? Category);

public sealed record IngredientAutocompleteDto(IReadOnlyList<IngredientSuggestionDto> Items);
