using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Plans;

public static class PlanEndpoints
{
    private const string WeekStartFormat = "yyyy-MM-dd";

    private const string ConflictMessage =
        "План изменил другой участник. Загрузите актуальную версию и повторите.";

    public static IEndpointRouteBuilder MapPlanEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/plans").RequireAuthorization();

        group.MapGet("/week/{weekStart}", GetWeekAsync);
        group.MapPut("/week/{weekStart}", SaveWeekAsync).RequireVerifiedEmail();
        group.MapDelete("/week/{weekStart}", DeleteWeekAsync).RequireVerifiedEmail();

        return app;
    }

    private static async Task<IResult> GetWeekAsync(
        string weekStart,
        ClaimsPrincipal principal,
        CurrentUserContext currentUser,
        WeekPlanReader reader)
    {
        var familyId = await currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return Results.NotFound(new PlanErrorDto("Вы не состоите в семье."));

        if (!TryParseWeekStart(weekStart, out var monday))
            return Results.BadRequest(new PlanErrorDto(
                $"Некорректная дата начала недели. Ожидается дата понедельника в формате {WeekStartFormat}."));

        var dto = await reader.ReadAsync(familyId.Value, monday) ?? WeekPlanReader.Empty(monday);
        return Results.Json(dto);
    }

    private static async Task<IResult> SaveWeekAsync(
        string weekStart,
        SaveWeekPlanRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        CurrentUserContext currentUser,
        WeekPlanSaver saver,
        WeekPlanReader reader)
    {
        var familyId = await currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return Results.NotFound(new PlanErrorDto("Вы не состоите в семье."));

        if (!TryParseWeekStart(weekStart, out var monday))
            return Results.BadRequest(new PlanErrorDto(
                $"Некорректная дата начала недели. Ожидается дата понедельника в формате {WeekStartFormat}."));

        var entries = request.Entries ?? new List<PlanEntryRequest>();
        var validationError = Validate(entries);
        if (validationError is not null)
            return Results.BadRequest(validationError);

        var recipeIds = entries.Select(e => e.RecipeId).Distinct().ToList();
        if (recipeIds.Count > 0)
        {
            var found = await db.Recipes.CountAsync(r => r.FamilyId == familyId.Value && recipeIds.Contains(r.Id));
            if (found != recipeIds.Count)
                return Results.BadRequest(new PlanErrorDto("Один или несколько рецептов не принадлежат вашей семье."));
        }

        var outcome = await saver.SaveAsync(
            familyId.Value, monday, entries, request.ExpectedRevision, DateTime.UtcNow);
        if (outcome == WeekPlanMutationOutcome.Conflict)
            return await ConflictAsync(reader, familyId.Value, monday);

        var saved = await reader.ReadAsync(familyId.Value, monday)
            ?? throw new InvalidOperationException("Сохранённый план недели не найден.");
        return Results.Json(saved);
    }

    private static async Task<IResult> DeleteWeekAsync(
        string weekStart,
        int? expectedRevision,
        ClaimsPrincipal principal,
        CurrentUserContext currentUser,
        WeekPlanSaver saver,
        WeekPlanReader reader)
    {
        var familyId = await currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return Results.NotFound(new PlanErrorDto("Вы не состоите в семье."));

        if (!TryParseWeekStart(weekStart, out var monday))
            return Results.BadRequest(new PlanErrorDto(
                $"Некорректная дата начала недели. Ожидается дата понедельника в формате {WeekStartFormat}."));

        // Без ожидаемой ревизии удаление защищено только для уже пустой недели:
        // повторное удаление без новых данных идемпотентно, а существующий
        // план так снять нельзя — сообщаем конфликт с текущей версией.
        if (expectedRevision is null)
        {
            return await reader.ExistsAsync(familyId.Value, monday)
                ? await ConflictAsync(reader, familyId.Value, monday)
                : Results.NoContent();
        }

        var outcome = await saver.DeleteAsync(familyId.Value, monday, expectedRevision.Value);
        if (outcome == WeekPlanMutationOutcome.Conflict)
            return await ConflictAsync(reader, familyId.Value, monday);

        return Results.NoContent();
    }

    /// <summary>Конфликт ревизии недели с актуальной серверной карточкой плана.</summary>
    private static async Task<IResult> ConflictAsync(WeekPlanReader reader, Guid familyId, DateOnly monday)
    {
        var dto = await reader.ReadAsync(familyId, monday) ?? WeekPlanReader.Empty(monday);
        return Results.Json(
            new PlanConflictDto(ConflictMessage, dto.WeekStart, dto.Revision, dto.Entries),
            statusCode: StatusCodes.Status409Conflict);
    }

    private static PlanErrorDto? Validate(IReadOnlyList<PlanEntryRequest> entries)
    {
        if (entries.Count > PlanningCatalog.EntriesMax)
            return new(
                $"В плане не может быть больше {PlanningCatalog.EntriesMax} записей.",
                "plan_entries_too_many",
                "entries");

        var seen = new HashSet<(int Day, string MealType)>();

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var field = $"entries[{index}]";

            // Malformed payload может прислать null вместо записи: отказ должен быть
            // контролируемым, а не исключением обращения к null.
            if (entry is null)
                return new("Запись плана пуста.", "plan_entry_null", field);

            if (entry.Day < PlanningCatalog.DayMin || entry.Day > PlanningCatalog.DayMax)
                return new(
                    $"Номер дня недели должен быть от {PlanningCatalog.DayMin} до {PlanningCatalog.DayMax}.",
                    "plan_day_range",
                    $"{field}.day");
            if (!PlanningCatalog.IsKnownMealType(entry.MealType))
                return new($"Недопустимый приём пищи «{entry.MealType}».", "plan_meal_type_invalid", $"{field}.mealType");
            if (entry.Portions < PlanningCatalog.PortionsMin || entry.Portions > PlanningCatalog.PortionsMax)
                return new(
                    $"Количество порций должно быть от {PlanningCatalog.PortionsMin} до {PlanningCatalog.PortionsMax}.",
                    "plan_portions_range",
                    $"{field}.portions");
            if (!seen.Add((entry.Day, entry.MealType)))
                return new(
                    "В плане не может быть двух записей для одного дня и приёма пищи.",
                    "plan_duplicate_slot",
                    field);
        }

        return null;
    }

    private static bool TryParseWeekStart(string value, out DateOnly monday)
    {
        monday = default;
        if (!DateOnly.TryParseExact(value, WeekStartFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return false;

        monday = date;
        return date.DayOfWeek == DayOfWeek.Monday;
    }
}
