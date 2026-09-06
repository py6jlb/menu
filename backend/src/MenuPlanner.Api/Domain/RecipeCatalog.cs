namespace MenuPlanner.Api.Domain;

public static class RecipeCatalog
{
    public const int NameMaxLength = 200;
    public const int TextMaxLength = 2000;
    public const int TagMaxLength = 50;
    public const int DietMaxLength = 100;
    public const int IngredientNameMaxLength = 200;
    public const int NoteMaxLength = 500;
    public const int CookTimeMin = 1;
    public const int CookTimeMax = 1440;
    public const int ServingsMin = 1;
    public const int ServingsMax = 100;
    public const int DifficultyMin = 1;
    public const int DifficultyMax = 5;
    public const int CaloriesMax = 10000;

    public const int PhotoMaxBytes = 5 * 1024 * 1024;
    public static readonly IReadOnlyDictionary<string, string> PhotoContentTypes =
        new Dictionary<string, string>
        {
            ["image/jpeg"] = ".jpg",
            ["image/png"] = ".png",
            ["image/webp"] = ".webp",
            ["image/gif"] = ".gif"
        };

    public static readonly IReadOnlyList<string> Units =
        new[] { "g", "kg", "ml", "l", "pcs", "glass", "tbsp", "tsp", "pinch" };

    public static readonly IReadOnlyList<string> Seasons =
        new[] { "winter", "spring", "summer", "autumn" };
}