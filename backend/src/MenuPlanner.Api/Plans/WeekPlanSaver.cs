using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Plans;

/// <summary>Исход ревизионной мутации недели.</summary>
public enum WeekPlanMutationOutcome
{
    Success,
    Conflict
}

/// <summary>
/// Атомарное сохранение и удаление недельного плана с проверкой ожидаемой
/// ревизии. Полная замена записей и смена ревизии выполняются в одной
/// транзакции, поэтому два сохранения от одной ревизии дают один успех и
/// один конфликт, а не смешение двух наборов записей.
/// </summary>
public sealed class WeekPlanSaver
{
    private readonly AppDbContext _db;

    public WeekPlanSaver(AppDbContext db) => _db = db;

    public async Task<WeekPlanMutationOutcome> SaveAsync(
        Guid familyId,
        DateOnly weekStart,
        IReadOnlyList<PlanEntryRequest> entries,
        int expectedRevision,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var plan = await _db.WeekPlans
            .Include(p => p.Entries)
            .FirstOrDefaultAsync(p => p.FamilyId == familyId && p.WeekStart == weekStart, cancellationToken);

        if (plan is null)
        {
            // Плана нет — создать его можно только от исходной ревизии.
            if (expectedRevision != WeekPlanRevisions.Initial)
                return WeekPlanMutationOutcome.Conflict;

            plan = new WeekPlan
            {
                Id = Guid.NewGuid(),
                FamilyId = familyId,
                WeekStart = weekStart,
                Revision = WeekPlanRevisions.Initial + 1,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.WeekPlans.Add(plan);
        }
        else
        {
            if (plan.Revision != expectedRevision)
                return WeekPlanMutationOutcome.Conflict;

            plan.Revision += 1;
            plan.UpdatedAt = now;
            _db.PlanEntries.RemoveRange(plan.Entries);
        }

        foreach (var entry in entries)
        {
            _db.PlanEntries.Add(new PlanEntry
            {
                WeekPlanId = plan.Id,
                Day = entry.Day,
                MealType = PlanningCatalog.MealTypeFromCode(entry.MealType),
                RecipeId = entry.RecipeId,
                Portions = entry.Portions
            });
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Ревизия изменилась между чтением и записью или неделя создана
            // параллельно: уникальность семьи/даты и токен ревизии не дают
            // смешать записи. Транзакция откачена, трекер очищается — состояние
            // контекста непротиворечиво для последующего чтения конфликта.
            _db.ChangeTracker.Clear();
            return WeekPlanMutationOutcome.Conflict;
        }

        return WeekPlanMutationOutcome.Success;
    }

    public async Task<WeekPlanMutationOutcome> DeleteAsync(
        Guid familyId,
        DateOnly weekStart,
        int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var plan = await _db.WeekPlans
            .Include(p => p.Entries)
            .FirstOrDefaultAsync(p => p.FamilyId == familyId && p.WeekStart == weekStart, cancellationToken);

        // Повторное удаление без новых данных идемпотентно: удалять уже нечего.
        if (plan is null)
            return WeekPlanMutationOutcome.Success;

        if (plan.Revision != expectedRevision)
            return WeekPlanMutationOutcome.Conflict;

        _db.WeekPlans.Remove(plan);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return WeekPlanMutationOutcome.Conflict;
        }

        return WeekPlanMutationOutcome.Success;
    }
}
