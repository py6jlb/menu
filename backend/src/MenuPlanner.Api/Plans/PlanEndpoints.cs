using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Plans;

public static class PlanEndpoints
{
    private const string WeekStartFormat = "yyyy-MM-dd";

    public static IEndpointRouteBuilder MapPlanEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/plans").RequireAuthorization();

        group.MapGet("/week/{weekStart}", GetWeekAsync);
        group.MapPut("/week/{weekStart}", SaveWeekAsync);
        group.MapDelete("/week/{weekStart}", DeleteWeekAsync);

        return app;
    }

    private static async Task<IResult> GetWeekAsync(
        string weekStart,
        ClaimsPrincipal principal,
        AppDbContext db)
    {
        var familyId = await CurrentFamilyIdAsync(principal, db);
        if (familyId is null)
            return Results.NotFound(new PlanErrorDto("Вы не состоите в семье."));

        if (!TryParseWeekStart(weekStart, out var monday))
            return Results.BadRequest(new PlanErrorDto(
                $"Некорректная дата начала недели. Ожидается дата понедельника в формате {WeekStartFormat}."));

        var plan = await LoadPlanAsync(db, familyId.Value, monday);
        if (plan is null)
            return Results.Json(new WeekPlanDto(Format(monday), Array.Empty<PlanEntryDto>()));

        return Results.Json(ToDto(plan));
    }

    private static async Task<IResult> SaveWeekAsync(
        string weekStart,
        SaveWeekPlanRequest request,
        ClaimsPrincipal principal,
        AppDbContext db)
    {
        var familyId = await CurrentFamilyIdAsync(principal, db);
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

        return Results.Json(ToDto(saved));
    }

    private static async Task<IResult> DeleteWeekAsync(
        string weekStart,
        ClaimsPrincipal principal,
        AppDbContext db)
    {
        var familyId = await CurrentFamilyIdAsync(principal, db);
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

    private static WeekPlanDto ToDto(WeekPlan plan)
    {
        var entries = plan.Entries
            .OrderBy(e => e.Day)
            .ThenBy(e => PlanningCatalog.OrderOf(e.MealType))
            .Select(e => new PlanEntryDto(
                e.Day,
                PlanningCatalog.CodeOf(e.MealType),
                e.RecipeId,
                e.Recipe?.Name ?? "",
                e.Portions))
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
