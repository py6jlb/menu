namespace MenuPlanner.Api.Domain;

public sealed class Recipe
{
    public Guid Id { get; set; }
    public Guid FamilyId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? PhotoPath { get; set; }
    public int CookTimeMinutes { get; set; }
    public int Servings { get; set; }
    public int Difficulty { get; set; }
    public int? Calories { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string> Seasonality { get; set; } = new();
    public List<string> Diet { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Family? Family { get; set; }
    public List<RecipeStep> Steps { get; set; } = new();
    public List<RecipeIngredient> Ingredients { get; set; } = new();
}