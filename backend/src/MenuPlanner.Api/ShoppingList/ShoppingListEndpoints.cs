using System.Security.Claims;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Auth.Sessions;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Plans;

namespace MenuPlanner.Api.ShoppingList;

/// <summary>Маршруты группы /api/shopping-list: список покупок на неделю.</summary>
public static class ShoppingListEndpoints
{
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

        if (!WeekStart.TryParse(weekStart, out var monday))
            return Results.BadRequest(new ShoppingListErrorDto(
                $"Некорректная дата начала недели. Ожидается дата понедельника в формате {WeekStart.FormatPattern}."));

        var plan = await reader.ReadContentAsync(familyId.Value, monday);
        if (plan is null)
        {
            return Results.Json(new ShoppingListDto(
                WeekStart.Format(monday), HasPlan: false, Array.Empty<ShoppingListItemDto>(),
                Array.Empty<ShoppingListExcludedDto>()));
        }

        var content = ShoppingListContentBuilder.Build(plan);

        var items = content.Items
            .Select(i => new ShoppingListItemDto(i.Name, i.Amount, i.Unit, i.Display, i.Category))
            .ToList();
        var excluded = content.Excluded
            .Select(e => new ShoppingListExcludedDto(e.Day, e.MealType, e.RecipeId, e.RecipeName, e.Reason))
            .ToList();

        return Results.Json(new ShoppingListDto(WeekStart.Format(monday), HasPlan: true, items, excluded));
    }
}
