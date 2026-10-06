using MenuPlanner.Api.Plans;
using MenuPlanner.Api.Recipes.External;

namespace MenuPlanner.Api.ShoppingList;

/// <summary>
/// Запись плана, исключённая из расчёта закупки: где она стоит и почему посчитать нельзя.
/// </summary>
public sealed record ExcludedPlanEntry(
    int Day,
    string MealType,
    Guid RecipeId,
    string RecipeName,
    string Reason);

/// <summary>Результат расчёта закупки: агрегированные позиции и диагностика полноты.</summary>
public sealed record ShoppingListContent(
    IReadOnlyList<ShoppingListItem> Items,
    IReadOnlyList<ExcludedPlanEntry> Excluded);

/// <summary>
/// Превращает разрешённый контент недели в закупку: считает живые ингредиенты источников,
/// масштабирует по порциям и отдельно возвращает записи, которые посчитать нельзя.
/// Сломанная ссылка исключается из сумм и попадает в диагностику — устаревшие
/// ингредиенты обёртки в расчёт не подставляются. Чистая функция: без БД и HTTP.
/// </summary>
public static class ShoppingListContentBuilder
{
    /// <summary>Причина исключения: рецепт-источник удалён, контент недоступен.</summary>
    public const string SourceMissingReason = "source_missing";

    public static ShoppingListContent Build(WeekPlanContent plan)
    {
        var lines = new List<IngredientLine>();
        var excluded = new List<ExcludedPlanEntry>();

        foreach (var entry in plan.Entries)
        {
            if (entry.State == ExternalRecipeState.Broken)
            {
                excluded.Add(new ExcludedPlanEntry(
                    entry.Day,
                    entry.MealType,
                    entry.RecipeId,
                    entry.RecipeName,
                    SourceMissingReason));
                continue;
            }

            var content = entry.Content;
            if (content is null || content.Servings <= 0)
                continue;

            foreach (var ingredient in content.Ingredients)
            {
                lines.Add(new IngredientLine(
                    ingredient.Name,
                    ShoppingListBuilder.Scale(ingredient.Amount, entry.Portions, content.Servings),
                    ingredient.Unit));
            }
        }

        return new ShoppingListContent(ShoppingListBuilder.Build(lines), excluded);
    }
}
