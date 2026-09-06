namespace MenuPlanner.Api.Domain;

public static class PlanningCatalog
{
    public const int DayMin = 0;
    public const int DayMax = 6;
    public const int PortionsMin = 1;
    public const int PortionsMax = 100;

    public static readonly IReadOnlyList<string> MealTypeCodes =
        new[] { "breakfast", "snack_1", "lunch", "snack_2", "dinner" };

    private static readonly IReadOnlyDictionary<string, MealType> Codes = new Dictionary<string, MealType>
    {
        ["breakfast"] = MealType.Breakfast,
        ["snack_1"] = MealType.Snack1,
        ["lunch"] = MealType.Lunch,
        ["snack_2"] = MealType.Snack2,
        ["dinner"] = MealType.Dinner
    };

    public static bool IsKnownMealType(string code) => Codes.ContainsKey(code);

    public static MealType MealTypeFromCode(string code) =>
        Codes.TryGetValue(code, out var mealType)
            ? mealType
            : throw new InvalidOperationException($"Неизвестный код приёма пищи: «{code}».");

    public static string CodeOf(MealType mealType)
    {
        foreach (var pair in Codes)
            if (pair.Value == mealType)
                return pair.Key;

        throw new InvalidOperationException($"Неизвестный приём пищи: {mealType}.");
    }

    public static int OrderOf(MealType mealType)
    {
        for (var index = 0; index < MealTypeCodes.Count; index++)
            if (MealTypeFromCode(MealTypeCodes[index]) == mealType)
                return index;

        throw new InvalidOperationException($"Неизвестный приём пищи: {mealType}.");
    }
}
