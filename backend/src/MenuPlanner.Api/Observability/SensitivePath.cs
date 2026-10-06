using System.Text.RegularExpressions;

namespace MenuPlanner.Api.Observability;

/// <summary>
/// Минимизация URL в журналах: share-токен в пути заменяется плейсхолдером,
/// поэтому ни access-логи, ни записи об исключениях не раскрывают ссылку.
/// </summary>
public static partial class SensitivePath
{
    public const string TokenPlaceholder = "{token}";

    /// <summary>Скрыть share-токены в путях публичного просмотра и импорта.</summary>
    public static string Minimize(string path) =>
        SharedTokenRegex().Replace(path, $"$1{TokenPlaceholder}");

    /// <summary>Шаблон маршрута надёжнее сырого пути; сырой путь минимизируется.</summary>
    public static string ForLogging(string? routePattern, string path) =>
        string.IsNullOrWhiteSpace(routePattern) ? Minimize(path) : routePattern;

    [GeneratedRegex(@"^(/(?:api/shared|r)/)[^/?#]+")]
    private static partial Regex SharedTokenRegex();
}
