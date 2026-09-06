namespace MenuPlanner.Api.Domain;

public sealed class RecipeStep
{
    public Guid Id { get; set; }
    public Guid RecipeId { get; set; }
    public int Order { get; set; }
    public required string Text { get; set; }

    public Recipe? Recipe { get; set; }
}