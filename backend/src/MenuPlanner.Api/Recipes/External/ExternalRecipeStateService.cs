namespace MenuPlanner.Api.Recipes.External;

/// <summary>
/// Состояние внешнего рецепта (обёртки) относительно его источника.
/// </summary>
public enum ExternalRecipeState
{
    /// <summary>Источник жив, ссылка актуальна — контент доступен.</summary>
    Ok,

    /// <summary>Источник жив, но ссылка отозвана/перегенерирована — данные ещё можно спасти копией.</summary>
    Warning,

    /// <summary>Источник удалён — контент недоступен, остаётся только кэш имени.</summary>
    Broken
}

/// <summary>
/// Чистое правило состояния внешнего рецепта в стиле <c>RepetitionService</c>.
/// Не знает про HTTP и БД: вызывающий сам сообщает, жив ли источник, совпадает ли
/// сохранённый обёрткой токен с текущим токеном шеринга и не отозван ли шеринг.
/// </summary>
public static class ExternalRecipeStateService
{
    /// <summary>
    /// Единое правило предупреждения: источник удалён → «сломанная»; иначе, если токен
    /// обёртки не совпадает с текущим токеном шеринга или шеринг отозван → «отозванная»;
    /// иначе всё в порядке.
    /// </summary>
    public static ExternalRecipeState Resolve(
        bool sourceExists,
        bool tokenMatches,
        bool shareRevoked)
    {
        if (!sourceExists)
            return ExternalRecipeState.Broken;
        if (!tokenMatches || shareRevoked)
            return ExternalRecipeState.Warning;
        return ExternalRecipeState.Ok;
    }

    public static string Code(ExternalRecipeState state) => state switch
    {
        ExternalRecipeState.Ok => "ok",
        ExternalRecipeState.Warning => "warning",
        _ => "broken"
    };
}
