namespace MenuPlanner.Api.Domain;

/// <summary>Тип одноразового кода: подтверждение почты или сброс пароля.</summary>
public enum AuthCodeType
{
    Verify,
    Reset
}

/// <summary>Одноразовый код пользователя: хэш, срок действия, признак использования и счётчик попыток.</summary>
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
    public int Attempts { get; set; }
}
