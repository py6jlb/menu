namespace MenuPlanner.Api.Domain;

/// <summary>Стандартная диета: канонический код и русская подпись для формы.</summary>
public sealed record DietOption(string Code, string Label);

/// <summary>
/// Стандартные диеты и их канонизация. Диета — свободный набор меток, но известные
/// варианты (код, русская подпись, частые формы) сводятся к каноническому коду, а
/// неизвестные метки сохраняются как есть — без перевода «по догадке».
/// Нормализация применяется на записи, чтении и подборе, поэтому один и тот же смысл
/// значения используется формой, жёсткими фильтрами и мягкими предпочтениями.
/// </summary>
public static class DietCatalog
{
    public static readonly IReadOnlyList<DietOption> Standard = new[]
    {
        new DietOption("vegetarian", "Вегетарианское"),
        new DietOption("gluten_free", "Безглютеновое"),
        new DietOption("lean", "Постное"),
        new DietOption("keto", "Кетогенное")
    };

    private static readonly IReadOnlyDictionary<string, string> Aliases = BuildAliases();

    /// <summary>
    /// Каноническое значение метки: известный вариант (код или русская форма) — код;
    /// неизвестный — исходный текст без внешних пробелов. Пустое значение — пустая строка.
    /// </summary>
    public static string Normalize(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return string.Empty;

        return Aliases.TryGetValue(trimmed, out var code) ? code : trimmed;
    }

    /// <summary>
    /// Нормализует набор меток, отбрасывает пустые и повторяющиеся (без учёта регистра).
    /// Повторный вызов на результате ничего не меняет — нормализация идемпотентна.
    /// </summary>
    public static List<string> NormalizeAll(IEnumerable<string>? values)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in values ?? Enumerable.Empty<string>())
        {
            var normalized = Normalize(value);
            if (normalized.Length == 0)
                continue;
            if (seen.Add(normalized))
                result.Add(normalized);
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> BuildAliases()
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Add(string code, params string[] variants)
        {
            aliases[code] = code;
            foreach (var variant in variants)
                aliases[variant] = code;
        }

        Add("vegetarian", "вегетарианское", "вегетарианский", "вегетарианская");
        Add("gluten_free", "gluten-free", "gluten free", "без глютена",
            "безглютеновое", "безглютеновый", "безглютеновая");
        Add("lean", "постное", "постный", "постная");
        Add("keto", "кетогенное", "кетогенный", "кетогенная");

        return aliases;
    }
}
