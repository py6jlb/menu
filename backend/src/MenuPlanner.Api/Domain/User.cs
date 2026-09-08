namespace MenuPlanner.Api.Domain;

public sealed class User
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public UserRole Role { get; set; } = UserRole.User;
    public DateTime CreatedAt { get; set; }
    public bool IsEmailVerified { get; set; }
    public DateTime? EmailVerifiedAt { get; set; }
    public int VerificationAttempts { get; set; }
    public DateTime? LockedUntil { get; set; }
    public int TokenVersion { get; set; }
}
