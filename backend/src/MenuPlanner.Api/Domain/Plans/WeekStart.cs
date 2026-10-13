using System.Globalization;

namespace MenuPlanner.Api.Domain.Plans;

/// <summary>
/// Чистое правило недели: разбор и формат даты понедельника, приведение произвольной
/// даты к началу её недели и расчёт окна из недель. Единый источник формата даты
/// начала недели для API и плана. Не знает про БД и HTTP, юнит-тестируется напрямую.
/// </summary>
public static class WeekStart
{
    /// <summary>Формат даты начала недели (<c>yyyy-MM-dd</c>).</summary>
    public const string FormatPattern = "yyyy-MM-dd";

    /// <summary>
    /// Разбор даты начала недели: строгий формат <see cref="FormatPattern"/> и
    /// требование, чтобы дата была понедельником.
    /// </summary>
    public static bool TryParse(string value, out DateOnly monday)
    {
        monday = default;
        if (!DateOnly.TryParseExact(value, FormatPattern, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return false;

        monday = date;
        return date.DayOfWeek == DayOfWeek.Monday;
    }

    /// <summary>Форматирование даты начала недели в <see cref="FormatPattern"/>.</summary>
    public static string Format(DateOnly date) =>
        date.ToString(FormatPattern, CultureInfo.InvariantCulture);

    /// <summary>Понедельник недели, которой принадлежит дата. Неделя начинается в понедельник.</summary>
    public static DateOnly Start(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }

    /// <summary>
    /// Окно из <paramref name="weeks"/> недель включительно, заканчивающееся выбранной
    /// неделей. Конец — сама выбранная неделя, начало — минус (weeks − 1) недель.
    /// </summary>
    public static (DateOnly Start, DateOnly End) Window(DateOnly selectedWeekStart, int weeks)
    {
        var start = selectedWeekStart.AddDays(-(weeks - 1) * 7);
        return (start, selectedWeekStart);
    }
}
