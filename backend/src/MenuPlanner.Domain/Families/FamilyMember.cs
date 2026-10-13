using MenuPlanner.Domain.Accounts;

namespace MenuPlanner.Domain.Families;

/// <summary>Членство пользователя в семье (составной ключ FamilyId + UserId).</summary>
public sealed class FamilyMember
{
    public Guid FamilyId { get; set; }
    public Guid UserId { get; set; }
    public DateTime JoinedAt { get; set; }

    public Family? Family { get; set; }
    public User? User { get; set; }
}
