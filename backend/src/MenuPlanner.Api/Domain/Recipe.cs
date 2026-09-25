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

    // External recipe linkage: a wrapper Recipe in the recipient family points at a source
    // Recipe that stays owned by the source family (ADR-0001). Only Name is cached here;
    // the rest of the content is read live from the source.
    public Guid? SourceRecipeId { get; set; }
    public Guid? SourceFamilyId { get; set; }
    public string? SourceToken { get; set; }

    public Family? Family { get; set; }
    public List<RecipeStep> Steps { get; set; } = new();
    public List<RecipeIngredient> Ingredients { get; set; } = new();
}