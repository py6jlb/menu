namespace MenuPlanner.Api.Domain;

public sealed class RecipeIngredient
{
    public Guid Id { get; set; }
    public Guid RecipeId { get; set; }
    public int Order { get; set; }
    public required string Name { get; set; }
    public decimal Amount { get; set; }
    public required string Unit { get; set; }
    public string? Note { get; set; }

    /// <summary>
    /// Код категории продукта из <see cref="RecipeCatalog.IngredientCategories"/> или null
    /// для «Прочего». Категория — свойство ингредиента, а не отдельного продукта.
    /// </summary>
    public string? Category { get; set; }

    public Recipe? Recipe { get; set; }
}