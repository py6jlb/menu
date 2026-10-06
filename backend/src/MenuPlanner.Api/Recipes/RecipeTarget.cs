namespace MenuPlanner.Api.Recipes;

/// <summary>
/// Идентичность рецепта в рамках семьи: пара, которая всегда путешествует вместе.
/// Ожидаемая ревизия передаётся отдельно — это токен конкурентности, а не часть
/// идентичности ресурса.
/// </summary>
public readonly record struct RecipeTarget(Guid Id, Guid FamilyId);
