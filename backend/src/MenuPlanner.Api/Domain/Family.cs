namespace MenuPlanner.Api.Domain;

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
}
