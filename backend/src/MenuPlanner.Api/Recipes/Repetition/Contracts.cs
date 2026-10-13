namespace MenuPlanner.Api.Recipes.Repetition;

/// <summary>Число повторений рецепта в сохранённых планах.</summary>
public sealed record RecipeRepetitionDto(Guid RecipeId, int Count);