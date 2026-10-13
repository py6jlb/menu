using MenuPlanner.Api.Domain.Accounts;
using MenuPlanner.Api.Domain.Plans;
using MenuPlanner.Api.Domain.Recipes;

namespace MenuPlanner.Api.Domain.Families;

/// <summary>Семья: владелец, инвайт-код и связанные участники, рецепты и недельные планы.</summary>
public sealed class Family
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string InviteCode { get; set; }
    public Guid OwnerId { get; set; }
    public DateTime CreatedAt { get; set; }

    public User? Owner { get; set; }
    public List<FamilyMember> Members { get; set; } = new();
    public List<Recipe> Recipes { get; set; } = new();
    public List<WeekPlan> WeekPlans { get; set; } = new();
}
