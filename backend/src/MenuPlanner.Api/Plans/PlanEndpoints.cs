using System.Security.Claims;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Plans;

public static class PlanEndpoints
{
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

        if (!WeekStart.TryParse(weekStart, out var monday))
            return Results.BadRequest(new PlanErrorDto(
                $"Некорректная дата начала недели. Ожидается дата понедельника в формате {WeekStart.FormatPattern}."));

        var dto = await reader.ReadAsync(familyId.Value, monday) ?? WeekPlanReader.Empty(monday);
        return Results.Json(dto);
    }

    private static async Task<IResult> SaveWeekAsync(
        string weekStart,
        SaveWeekPlanRequest request,
        ClaimsPrincipal principal,
        CurrentUserContext currentUser,
        WeekPlanSaver saver,
        WeekPlanReader reader)
    {
        var familyId = await currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return Results.NotFound(new PlanErrorDto("Вы не состоите в семье."));

        if (!WeekStart.TryParse(weekStart, out var monday))
            return Results.BadRequest(new PlanErrorDto(
                $"Некорректная дата начала недели. Ожидается дата понедельника в формате {WeekStart.FormatPattern}."));

        var entries = request.Entries ?? new List<PlanEntryRequest>();
        var validationError = PlanValidation.ValidateEntries(entries);
        if (validationError is not null)
            return Results.BadRequest(validationError);

        var outcome = await saver.SaveAsync(
            familyId.Value, monday, entries, request.ExpectedRevision, DateTime.UtcNow);
        if (outcome == WeekPlanMutationOutcome.NotOwnedRecipes)
            return Results.BadRequest(new PlanErrorDto("Один или несколько рецептов не принадлежат вашей семье."));
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

        if (!WeekStart.TryParse(weekStart, out var monday))
            return Results.BadRequest(new PlanErrorDto(
                $"Некорректная дата начала недели. Ожидается дата понедельника в формате {WeekStart.FormatPattern}."));

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
}
