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
/// Не знает про HTTP и БД: вызывающий сам решает, жив ли источник.
/// </summary>
public static class ExternalRecipeStateService
{
    public static ExternalRecipeState Resolve(bool sourceExists)
        => sourceExists ? ExternalRecipeState.Ok : ExternalRecipeState.Broken;

    public static string Code(ExternalRecipeState state) => state switch
    {
        ExternalRecipeState.Ok => "ok",
        ExternalRecipeState.Warning => "warning",
        _ => "broken"
    };
}
