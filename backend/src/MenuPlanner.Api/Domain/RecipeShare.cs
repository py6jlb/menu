namespace MenuPlanner.Api.Domain;

/// <summary>Публичная ссылка на рецепт: токен и момент отзыва.</summary>
public sealed class RecipeShare
{
    public Guid Id { get; set; }
    public Guid RecipeId { get; set; }
    public required string Token { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public Recipe? Recipe { get; set; }
}
