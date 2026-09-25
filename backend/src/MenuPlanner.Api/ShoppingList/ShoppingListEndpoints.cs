using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
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
        AppDbContext db)
    {
        var familyId = await CurrentFamilyIdAsync(principal, db);
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

        var states = await ExternalRecipeStateResolver.ResolveManyAsync(
            db,
            plan.Entries
                .Where(e => e.Recipe?.SourceRecipeId is not null)
                .Select(e => new ExternalSourceLink(
                    e.Recipe!.Id, e.Recipe.SourceRecipeId!.Value, e.Recipe.SourceToken))
                .ToList());

        var liveSources = await ExternalRecipeContentResolver.LoadSourcesAsync(
            db,
            plan.Entries
                .Where(e => e.Recipe?.SourceRecipeId is not null)
                .Select(e => e.Recipe!.SourceRecipeId!.Value));

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

    private static async Task<Guid?> CurrentFamilyIdAsync(ClaimsPrincipal principal, AppDbContext db)
    {
        var userId = UserIdFrom(principal);
        if (userId is null)
            return null;

        var membership = await db.FamilyMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId.Value);
        return membership?.FamilyId;
    }

    private static Guid? UserIdFrom(ClaimsPrincipal principal)
    {
        var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}