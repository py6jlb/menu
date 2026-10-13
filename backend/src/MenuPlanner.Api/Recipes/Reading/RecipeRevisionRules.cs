namespace MenuPlanner.Api.Recipes;

/// <summary>
/// Чистое правило проверяемой ревизии рецепта: без БД и HTTP. Чтение отдаёт
/// ревизию вместе с рецептом, изменение принимает ожидаемую и атомарно
/// проверяет, что она ещё актуальна.
/// </summary>
public static class RecipeRevisionRules
{
    /// <summary>Ревизия нового рецепта и значение по умолчанию после миграции.</summary>
    public const int Initial = 1;

    public const string MissingMessage =
        "Не указана версия рецепта. Загрузите рецепт заново и повторите сохранение.";

    public const string ConflictMessage =
        "Рецепт изменён другим участником. Черновик сохранён — загрузите актуальную версию, сравните изменения и сохраните снова.";

    /// <summary>Совпадает ли ожидаемая ревизия с текущей.</summary>
    public static bool IsCurrent(int? expected, int actual) =>
        expected is not null && expected.Value == actual;

    /// <summary>Следующая ревизия после успешной правки.</summary>
    public static int Next(int current) => current + 1;
}
