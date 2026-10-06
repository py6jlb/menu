using System.Globalization;
using System.Security.Claims;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Plans;

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
        CurrentUserContext currentUser,
        WeekPlanReader reader)
    {
        var familyId = await currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return Results.NotFound(new ShoppingListErrorDto("Вы не состоите в семье."));

        if (!TryParseWeekStart(weekStart, out var monday))
            return Results.BadRequest(new ShoppingListErrorDto(
                $"Некорректная дата начала недели. Ожидается дата понедельника в формате {WeekStartFormat}."));

        var plan = await reader.ReadContentAsync(familyId.Value, monday);
        if (plan is null)
        {
            return Results.Json(new ShoppingListDto(
                Format(monday), HasPlan: false, Array.Empty<ShoppingListItemDto>(),
                Array.Empty<ShoppingListExcludedDto>()));
        }

        var content = ShoppingListContentBuilder.Build(plan);

        var items = content.Items
            .Select(i => new ShoppingListItemDto(i.Name, i.Amount, i.Unit, i.Display))
            .ToList();
        var excluded = content.Excluded
            .Select(e => new ShoppingListExcludedDto(e.Day, e.MealType, e.RecipeId, e.RecipeName, e.Reason))
            .ToList();

        return Results.Json(new ShoppingListDto(Format(monday), HasPlan: true, items, excluded));
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
