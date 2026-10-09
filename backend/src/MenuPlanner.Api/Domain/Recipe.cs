namespace MenuPlanner.Api.Domain;

public sealed class Recipe
{
    public Guid Id { get; set; }
    public Guid FamilyId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? PhotoPath { get; set; }

    // Необязательный PDF-документ с описанием рецепта. Позволяет хранить рецепт
    // без структурированных шагов и ингредиентов — их может не быть, а метод
    // бывает описан в самом документе. Читается живьём у внешнего источника.
    public string? DocumentPath { get; set; }
    public int CookTimeMinutes { get; set; }
    public int Servings { get; set; }
    public int Difficulty { get; set; }
    public int? Calories { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string> Seasonality { get; set; } = new();
    public List<string> Diet { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Проверяемая ревизия рецепта. Растёт на каждую успешную правку (контент, фото,
    // промоушен или удаление). Клиент читает её вместе с рецептом и передаёт обратно
    // при редактировании: изменение, основанное на устаревшей ревизии, не перезаписывает
    // чужую работу. Настроена как токен конкурентности EF Core.
    public int Revision { get; set; } = 1;

    // Связь внешнего рецепта: строка Recipe в семье-получателе ссылается на рецепт-источник,
    // который остаётся у семьи-источника (ADR-0001). Здесь кэшируется только Name;
    // остальной контент читается живьём из источника.
    public Guid? SourceRecipeId { get; set; }
    public Guid? SourceFamilyId { get; set; }
    public string? SourceToken { get; set; }

    // Метка происхождения, сохраняемая после промоушена в копию: «скопировано из семьи X».
    // Null для обычных рецептов и для внешних рецептов; стирается, как только пользователь
    // редактирует копию.
    public string? CopiedFromFamilyName { get; set; }

    public Family? Family { get; set; }
    public List<RecipeStep> Steps { get; set; } = new();
    public List<RecipeIngredient> Ingredients { get; set; } = new();
}