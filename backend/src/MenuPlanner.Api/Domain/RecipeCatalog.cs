namespace MenuPlanner.Api.Domain;

public static class RecipeCatalog
{
    public const int NameMaxLength = 200;
    public const int TextMaxLength = 2000;
    public const int TagMaxLength = 50;
    public const int DietMaxLength = 100;
    public const int IngredientNameMaxLength = 200;
    public const int UnitMaxLength = 32;
    public const int NoteMaxLength = 500;
    public const int IngredientCategoryMaxLength = 32;
    public const int CookTimeMin = 1;
    public const int CookTimeMax = 1440;
    public const int ServingsMin = 1;
    public const int ServingsMax = 100;
    public const int DifficultyMin = 1;
    public const int DifficultyMax = 5;
    public const int CaloriesMax = 10000;

    // Точность количества согласована с хранилищем: numeric(10,2) в PostgreSQL.
    // Больше двух знаков после запятой БД молча округлила бы, поэтому такое
    // значение отклоняется до записи. Хвостовые нули большей точностью не считаются.
    public const int AmountScale = 2;
    public const decimal AmountMax = 99_999_999.99m;

    // Пределы коллекций: защищают от чрезмерной полезной нагрузки до записи.
    public const int StepsMax = 500;
    public const int IngredientsMax = 500;
    public const int TagsMax = 100;
    public const int DietsMax = 100;

    public const int PhotoMaxBytes = 5 * 1024 * 1024;
    // Защита от «бомбы»: маленький файл с огромными размерами не должен
    // исчерпать память при декодировании.
    public const int PhotoMaxDimension = 8000;
    public const long PhotoMaxPixels = 25_000_000;
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

    // Категории продуктов для группировки списка покупок: конечный справочник,
    // порядок задаёт порядок отделов в магазине. Пустое значение (null) — «Прочее»,
    // отдельным кодом не выражается и в списке всегда идёт последним.
    public static readonly IReadOnlyList<string> IngredientCategories = new[]
    {
        "vegetables",
        "meat",
        "fish",
        "dairy",
        "bakery",
        "groceries",
        "sauces_spices",
        "oils_vinegar",
        "frozen",
        "sweets_snacks",
        "drinks"
    };

    /// <summary>Порядковый номер категории в справочнике; null (Прочее) — в конце.</summary>
    public static int IngredientCategoryRank(string? category)
    {
        if (category is null)
            return int.MaxValue;
        for (var i = 0; i < IngredientCategories.Count; i++)
        {
            if (IngredientCategories[i] == category)
                return i;
        }
        return int.MaxValue;
    }
}