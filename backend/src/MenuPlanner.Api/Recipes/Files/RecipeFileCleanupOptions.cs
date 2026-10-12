using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MenuPlanner.Api.Recipes.Files;

/// <summary>
/// Параметры фоновой уборки бесхозных файлов рецептов (все виды): включена ли она,
/// период между проходами и безопасный возраст файла. Возраст по умолчанию (24 ч)
/// заведомо превышает самое долгое окно незавершённой операции
/// (загрузка/копирование/backup); уменьшая его, оператор должен свериться с этим
/// окном.
/// </summary>
public sealed class RecipeFileCleanupOptions
{
    public const bool DefaultEnabled = true;
    public const int DefaultIntervalHours = 24;
    public const int DefaultMinimumAgeHours = 24;

    public bool Enabled { get; set; } = DefaultEnabled;
    public int IntervalHours { get; set; } = DefaultIntervalHours;
    public int MinimumAgeHours { get; set; } = DefaultMinimumAgeHours;

    public TimeSpan Interval => TimeSpan.FromHours(IntervalHours);

    public TimeSpan MinimumAge => TimeSpan.FromHours(MinimumAgeHours);

    /// <summary>
    /// Читает канонические ключи, а старые (<c>PHOTO_CLEANUP_*</c>) принимает как
    /// fallback с записью о deprecation, чтобы обновление развёрнутого стенда не
    /// изменило поведение уборки молча.
    /// </summary>
    public static RecipeFileCleanupOptions Read(IConfiguration configuration, ILogger logger)
    {
        var options = new RecipeFileCleanupOptions();
        options.Enabled = Bool(
            configuration, logger, "RECIPE_FILE_CLEANUP_ENABLED", "PHOTO_CLEANUP_ENABLED", options.Enabled);
        options.IntervalHours = Int(
            configuration, logger,
            "RECIPE_FILE_CLEANUP_INTERVAL_HOURS", "PHOTO_CLEANUP_INTERVAL_HOURS", options.IntervalHours);
        options.MinimumAgeHours = Int(
            configuration, logger,
            "RECIPE_FILE_CLEANUP_MIN_AGE_HOURS", "PHOTO_CLEANUP_MIN_AGE_HOURS", options.MinimumAgeHours);
        options.Validate();
        return options;
    }

    public void Validate()
    {
        if (IntervalHours < 1)
            throw new InvalidOperationException(
                "Конфигурация: RECIPE_FILE_CLEANUP_INTERVAL_HOURS должен быть ≥ 1");
        if (MinimumAgeHours < 1)
            throw new InvalidOperationException(
                "Конфигурация: RECIPE_FILE_CLEANUP_MIN_AGE_HOURS должен быть ≥ 1");
    }

    private static bool Bool(
        IConfiguration configuration, ILogger logger, string key, string legacyKey, bool fallback)
    {
        if (bool.TryParse(configuration[key], out var value)) return value;
        if (bool.TryParse(configuration[legacyKey], out var legacy))
        {
            WarnDeprecated(logger, legacyKey, key, configuration[legacyKey]!);
            return legacy;
        }

        return fallback;
    }

    private static int Int(
        IConfiguration configuration, ILogger logger, string key, string legacyKey, int fallback)
    {
        if (int.TryParse(configuration[key], out var value)) return value;
        if (int.TryParse(configuration[legacyKey], out var legacy))
        {
            WarnDeprecated(logger, legacyKey, key, configuration[legacyKey]!);
            return legacy;
        }

        return fallback;
    }

    private static void WarnDeprecated(ILogger logger, string legacyKey, string key, string value) =>
        logger.LogWarning(
            "Конфигурация: {LegacyKey} устарел, используйте {Key}. Прочитано значение {Value}.",
            legacyKey, key, value);
}
