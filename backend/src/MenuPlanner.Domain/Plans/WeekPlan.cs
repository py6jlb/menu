using MenuPlanner.Domain.Families;

namespace MenuPlanner.Domain.Plans;

/// <summary>Недельный план семьи: начало недели, ревизия и записи.</summary>
public sealed class WeekPlan
{
    public Guid Id { get; set; }
    public Guid FamilyId { get; set; }
    public DateOnly WeekStart { get; set; }

    /// <summary>
    /// Ревизия всей недели: растёт на каждое сохранение и защищает от
    /// перезаписи чужой работы устаревшим запросом. У ещё не созданного
    /// плана ревизия равна <see cref="WeekPlanRevisions.Initial"/>.
    /// </summary>
    public int Revision { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Family? Family { get; set; }
    public List<PlanEntry> Entries { get; set; } = new();
}
