using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Recipes.External;

namespace MenuPlanner.Api.ShoppingList;

public static class ShoppingListEndpoints
{
    private const string WeekStartFormat = "yyyy-MM-dd";

    public static IEndpointRouteBuilder MapShoppingListEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/shopping-list").RequireAuthorization();

        group.MapGet("/", GetAsync);

        return app;
    }

    private static async Task<IResult> GetAsync(
        string weekStart,
        ClaimsPrincipal principal,
        AppDbContext db,
        CurrentUserContext currentUser,
        ExternalRecipeStateResolver stateResolver,
        ExternalRecipeSourceLoader sourceLoader)
    {
        var familyId = await currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return Results.NotFound(new ShoppingListErrorDto("Вы не состоите в семье."));

        if (!TryParseWeekStart(weekStart, out var monday))
            return Results.BadRequest(new ShoppingListErrorDto(
                $"Некорректная дата начала недели. Ожидается дата понедельника в формате {WeekStartFormat}."));

        var plan = await db.WeekPlans
            .AsNoTracking()
            .Include(p => p.Entries)
                .ThenInclude(e => e.Recipe)
                    .ThenInclude(r => r.Ingredients)
            .FirstOrDefaultAsync(p => p.FamilyId == familyId.Value && p.WeekStart == monday);

        if (plan is null)
            return Results.Json(new ShoppingListDto(Format(monday), Array.Empty<ShoppingListItemDto>()));

        var states = await stateResolver.ResolveManyAsync(
            ExternalPlanContent.SourceLinks(plan.Entries));

        var liveSources = await sourceLoader.LoadSourcesAsync(
            ExternalPlanContent.SourceRecipeIds(plan.Entries));

        var lines = new List<IngredientLine>();
        foreach (var entry in plan.Entries)
        {
            var recipe = entry.Recipe;
            if (recipe is null)
                continue;

            // Сломанная ссылка: источник удалён, контент недоступен — из закупки исключаем.
            if (states.TryGetValue(recipe.Id, out var state) && state == ExternalRecipeState.Broken)
                continue;

            // Живые ингредиенты и текущие порции источника для внешнего рецепта.
            var content = ExternalRecipeContentResolver.Resolve(recipe, liveSources);

            foreach (var ingredient in content.Ingredients)
            {
                lines.Add(new IngredientLine(
                    ingredient.Name,
                    ShoppingListBuilder.Scale(ingredient.Amount, entry.Portions, content.Servings),
                    ingredient.Unit));
            }
        }

        var items = ShoppingListBuilder.Build(lines)
            .Select(i => new ShoppingListItemDto(i.Name, i.Amount, i.Unit, i.Display))
            .ToList();

        return Results.Json(new ShoppingListDto(Format(monday), items));
    }

    private static bool TryParseWeekStart(string value, out DateOnly monday)
    {
        monday = default;
        if (!DateOnly.TryParseExact(value, WeekStartFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return false;

        monday = date;
        return date.DayOfWeek == DayOfWeek.Monday;
    }

    private static string Format(DateOnly date) =>
        date.ToString(WeekStartFormat, CultureInfo.InvariantCulture);

}
