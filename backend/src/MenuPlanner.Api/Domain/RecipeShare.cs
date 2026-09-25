namespace MenuPlanner.Api.Domain;

public sealed class RecipeShare
{
    public Guid Id { get; set; }
    public Guid RecipeId { get; set; }
    public required string Token { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public Recipe? Recipe { get; set; }
}
