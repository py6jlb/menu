namespace MenuPlanner.Api.Domain;

/// <summary>
/// Чистое правило категории продукта. Категория агрегированной позиции (или подсказки
/// автодополнения) определяется по большинству слагаемых ингредиентов; при равенстве
/// голосов или их отсутствии — «Прочее» (null). Одно правило для списка покупок и
/// подсказок, чтобы они не расходились. Статический класс без состояния.
/// </summary>
public static class IngredientCategoryRules
{
    /// <summary>
    /// Категория по большинству голосов: единоличный лидер, иначе null («Прочее»).
    /// Голоса — количество ингредиентов (или суммарная частота), приписавших категорию.
    /// </summary>
    public static string? ResolveMajority(IReadOnlyDictionary<string, int> votes)
    {
        if (votes.Count == 0)
            return null;

        var max = votes.Values.Max();
        var leaders = votes.Where(vote => vote.Value == max).Select(vote => vote.Key).ToList();
        return leaders.Count == 1 ? leaders[0] : null;
    }
}
