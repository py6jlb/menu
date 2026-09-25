using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Recipes.External;

namespace MenuPlanner.Api.Plans;

public static class PlanEndpoints
{
    private const string WeekStartFormat = "yyyy-MM-dd";

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
        AppDbContext db)
    {
        var familyId = await CurrentUser.FamilyIdAsync(principal, db);
        if (familyId is null)
            return Results.NotFound(new PlanErrorDto("Вы не состоите в семье."));

        if (!TryParseWeekStart(weekStart, out var monday))
            return Results.BadRequest(new PlanErrorDto(
                $"Некорректная дата начала недели. Ожидается дата понедельника в формате {WeekStartFormat}."));

        var plan = await LoadPlanAsync(db, familyId.Value, monday);
        if (plan is null)
            return Results.Json(new WeekPlanDto(Format(monday), Array.Empty<PlanEntryDto>()));

        var states = await ResolveStatesAsync(db, plan);
        var liveSources = await LoadLiveSourcesAsync(db, plan);
        return Results.Json(ToDto(plan, states, liveSources));
    }

    private static async Task<IResult> SaveWeekAsync(
        string weekStart,
        SaveWeekPlanRequest request,
        ClaimsPrincipal principal,
        AppDbContext db)
    {
        var familyId = await CurrentUser.FamilyIdAsync(principal, db);
        if (familyId is null)
            return Results.NotFound(new PlanErrorDto("Вы не состоите в семье."));

        if (!TryParseWeekStart(weekStart, out var monday))
            return Results.BadRequest(new PlanErrorDto(
                $"Некорректная дата начала недели. Ожидается дата понедельника в формате {WeekStartFormat}."));

        var entries = request.Entries ?? new List<PlanEntryRequest>();
        var validationError = Validate(entries);
        if (validationError is not null)
            return Results.BadRequest(new PlanErrorDto(validationError));

        var recipeIds = entries.Select(e => e.RecipeId).Distinct().ToList();
        if (recipeIds.Count > 0)
        {
            var found = await db.Recipes.CountAsync(r => r.FamilyId == familyId.Value && recipeIds.Contains(r.Id));
            if (found != recipeIds.Count)
                return Results.BadRequest(new PlanErrorDto("Один или несколько рецептов не принадлежат вашей семье."));
        }

        var plan = await db.WeekPlans
            .Include(p => p.Entries)
            .FirstOrDefaultAsync(p => p.FamilyId == familyId.Value && p.WeekStart == monday);

        var now = DateTime.UtcNow;
        if (plan is null)
        {
            plan = new WeekPlan
            {
                Id = Guid.NewGuid(),
                FamilyId = familyId.Value,
                WeekStart = monday,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.WeekPlans.Add(plan);
        }
        else
        {
            db.PlanEntries.RemoveRange(plan.Entries);
            plan.UpdatedAt = now;
        }

        foreach (var entry in entries)
        {
            db.PlanEntries.Add(new PlanEntry
            {
                WeekPlanId = plan.Id,
                Day = entry.Day,
                MealType = PlanningCatalog.MealTypeFromCode(entry.MealType),
                RecipeId = entry.RecipeId,
                Portions = entry.Portions
            });
        }

        await db.SaveChangesAsync();

        var saved = await LoadPlanAsync(db, familyId.Value, monday)
            ?? throw new InvalidOperationException("Сохранённый план недели не найден.");

        var states = await ResolveStatesAsync(db, saved);
        var liveSources = await LoadLiveSourcesAsync(db, saved);
        return Results.Json(ToDto(saved, states, liveSources));
    }

    private static async Task<IResult> DeleteWeekAsync(
        string weekStart,
        ClaimsPrincipal principal,
        AppDbContext db)
    {
        var familyId = await CurrentUser.FamilyIdAsync(principal, db);
        if (familyId is null)
            return Results.NotFound(new PlanErrorDto("Вы не состоите в семье."));

        if (!TryParseWeekStart(weekStart, out var monday))
            return Results.BadRequest(new PlanErrorDto(
                $"Некорректная дата начала недели. Ожидается дата понедельника в формате {WeekStartFormat}."));

        var plan = await db.WeekPlans
            .FirstOrDefaultAsync(p => p.FamilyId == familyId.Value && p.WeekStart == monday);
        if (plan is not null)
        {
            db.WeekPlans.Remove(plan);
            await db.SaveChangesAsync();
        }

        return Results.NoContent();
    }

    private static string? Validate(IReadOnlyList<PlanEntryRequest> entries)
    {
        var seen = new HashSet<(int Day, string MealType)>();

        foreach (var entry in entries)
        {
            if (entry.Day < PlanningCatalog.DayMin || entry.Day > PlanningCatalog.DayMax)
                return $"Номер дня недели должен быть от {PlanningCatalog.DayMin} до {PlanningCatalog.DayMax}.";
            if (!PlanningCatalog.IsKnownMealType(entry.MealType))
                return $"Недопустимый приём пищи «{entry.MealType}».";
            if (entry.Portions < PlanningCatalog.PortionsMin || entry.Portions > PlanningCatalog.PortionsMax)
                return $"Количество порций должно быть от {PlanningCatalog.PortionsMin} до {PlanningCatalog.PortionsMax}.";
            if (!seen.Add((entry.Day, entry.MealType)))
                return "В плане не может быть двух записей для одного дня и приёма пищи.";
        }

        return null;
    }

    private static async Task<WeekPlan?> LoadPlanAsync(AppDbContext db, Guid familyId, DateOnly weekStart)
    {
        return await db.WeekPlans
            .AsNoTracking()
            .Include(p => p.Entries)
            .ThenInclude(e => e.Recipe)
            .FirstOrDefaultAsync(p => p.FamilyId == familyId && p.WeekStart == weekStart);
    }

    private static async Task<Dictionary<Guid, ExternalRecipeState>> ResolveStatesAsync(
        AppDbContext db, WeekPlan plan)
    {
        var links = ExternalPlanContent.SourceLinks(plan.Entries);
        return await ExternalRecipeStateResolver.ResolveManyAsync(db, links);
    }

    private static Task<Dictionary<Guid, Recipe>> LoadLiveSourcesAsync(AppDbContext db, WeekPlan plan) =>
        ExternalRecipeContentResolver.LoadSourcesAsync(
            db, ExternalPlanContent.SourceRecipeIds(plan.Entries));

    private static WeekPlanDto ToDto(
        WeekPlan plan,
        IReadOnlyDictionary<Guid, ExternalRecipeState> states,
        IReadOnlyDictionary<Guid, Recipe> liveSources)
    {
        var entries = plan.Entries
            .OrderBy(e => e.Day)
            .ThenBy(e => PlanningCatalog.OrderOf(e.MealType))
            .Select(e => new PlanEntryDto(
                e.Day,
                PlanningCatalog.CodeOf(e.MealType),
                e.RecipeId,
                e.Recipe is null
                    ? ""
                    : ExternalRecipeContentResolver.Resolve(e.Recipe, liveSources).Name,
                e.Portions,
                states.TryGetValue(e.RecipeId, out var state)
                    ? ExternalRecipeStateService.Code(state)
                    : null))
            .ToList();

        return new WeekPlanDto(Format(plan.WeekStart), entries);
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
