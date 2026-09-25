using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Recipes.External;

/// <summary>
/// Проекции внешних рецептов из записей плана недели: ссылки на источники для вычисления
/// состояния и id источников для живого чтения контента.
/// </summary>
public static class ExternalPlanContent
{
    /// <summary>Ссылки внешних рецептов плана на их источники.</summary>
    public static List<ExternalSourceLink> SourceLinks(IEnumerable<PlanEntry> entries) =>
        entries
            .Where(e => e.Recipe?.SourceRecipeId is not null)
            .Select(e => new ExternalSourceLink(
                e.Recipe!.Id, e.Recipe.SourceRecipeId!.Value, e.Recipe.SourceToken))
            .ToList();

    /// <summary>Id источников внешних рецептов плана (для живого чтения контента).</summary>
    public static List<Guid> SourceRecipeIds(IEnumerable<PlanEntry> entries) =>
        entries
            .Where(e => e.Recipe?.SourceRecipeId is not null)
            .Select(e => e.Recipe!.SourceRecipeId!.Value)
            .ToList();
}
