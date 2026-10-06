using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Recipes.External;

/// <summary>
/// Название ингредиента, сведённое по нормализованному виду (trim/lowercase), с частотой
/// употребления в рамках выбранного набора рецептов.
/// </summary>
public sealed record IngredientUsage(string Normalized, string Name, int Usage);

/// <summary>
/// Скалярная агрегация названий ингредиентов для автодополнения. Пустые имена отсекаются,
/// поиск по префиксу и группировка по нормализованному названию перенесены в SQL: в память
/// попадают только различимые названия с частотами, а не все строки ингредиентов. За счёт
/// этого объём чтения не растёт вместе с содержимым семейной коллекции.
/// </summary>
public static class IngredientUsageQuery
{
    public static IQueryable<IngredientUsage> Build(
        IQueryable<RecipeIngredient> ingredients, string normalizedQuery)
    {
        if (normalizedQuery.Length > 0)
        {
            ingredients = ingredients.Where(i =>
                i.Name.Trim().ToLower().StartsWith(normalizedQuery));
        }

        return ingredients
            .Where(i => i.Name.Trim() != "")
            .GroupBy(i => i.Name.Trim().ToLower())
            .Select(g => new IngredientUsage(g.Key, g.Min(x => x.Name.Trim())!, g.Count()));
    }
}
