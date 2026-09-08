namespace MenuPlanner.Api.Domain;

public enum AuthCodeType
{
    Verify,
    Reset
}

public sealed class AuthCode
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public AuthCodeType Type { get; set; }
    public required string CodeHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool Used { get; set; }
}
