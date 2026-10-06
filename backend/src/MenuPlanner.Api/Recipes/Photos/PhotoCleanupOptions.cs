using Microsoft.Extensions.Configuration;

namespace MenuPlanner.Api.Recipes.Photos;

/// <summary>
/// Параметры фоновой уборки бесхозных фото: включена ли она, период между
/// проходами и безопасный возраст файла. Возраст по умолчанию (24 ч) заведомо
/// превышает самое долгое окно незавершённой операции (загрузка/копирование/
/// backup); уменьшая его, оператор должен свериться с этим окном.
/// </summary>
public sealed class PhotoCleanupOptions
{
    public const bool DefaultEnabled = true;
    public const int DefaultIntervalHours = 24;
    public const int DefaultMinimumAgeHours = 24;

    public bool Enabled { get; set; } = DefaultEnabled;
    public int IntervalHours { get; set; } = DefaultIntervalHours;
    public int MinimumAgeHours { get; set; } = DefaultMinimumAgeHours;

    public TimeSpan Interval => TimeSpan.FromHours(IntervalHours);

    public TimeSpan MinimumAge => TimeSpan.FromHours(MinimumAgeHours);

    public static PhotoCleanupOptions Read(IConfiguration configuration)
    {
        var options = new PhotoCleanupOptions();
        if (bool.TryParse(configuration["PHOTO_CLEANUP_ENABLED"], out var enabled))
            options.Enabled = enabled;
        options.IntervalHours = Int(configuration, "PHOTO_CLEANUP_INTERVAL_HOURS", options.IntervalHours);
        options.MinimumAgeHours = Int(configuration, "PHOTO_CLEANUP_MIN_AGE_HOURS", options.MinimumAgeHours);
        options.Validate();
        return options;
    }

    public void Validate()
    {
        if (IntervalHours < 1)
            throw new InvalidOperationException(
                "Конфигурация: PHOTO_CLEANUP_INTERVAL_HOURS должен быть ≥ 1");
        if (MinimumAgeHours < 1)
            throw new InvalidOperationException(
                "Конфигурация: PHOTO_CLEANUP_MIN_AGE_HOURS должен быть ≥ 1");
    }

    private static int Int(IConfiguration configuration, string key, int fallback) =>
        int.TryParse(configuration[key], out var value) ? value : fallback;
}
