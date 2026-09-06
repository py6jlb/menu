namespace MenuPlanner.Api.Domain;

public sealed class PlanEntry
{
    public Guid Id { get; set; }
    public Guid WeekPlanId { get; set; }
    public int Day { get; set; }
    public MealType MealType { get; set; }
    public Guid RecipeId { get; set; }
    public int Portions { get; set; }

    public WeekPlan? WeekPlan { get; set; }
    public Recipe? Recipe { get; set; }
}
