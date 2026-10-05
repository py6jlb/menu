using MenuPlanner.Api.Auth.Codes;

namespace MenuPlanner.Api.Auth;

public sealed class AuthCodeOptions
{
    public const string SectionName = "AuthCode";

    public const int DefaultMaxAttempts = AuthCodeService.MaxAttempts;
    public const int DefaultLockDurationDays = 3;
    public const int DefaultResendCooldownMinutes = 5;
    public const int DefaultResendRateLimitPerHour = 5;

    public int MaxAttempts { get; set; } = DefaultMaxAttempts;
    public int LockDurationDays { get; set; } = DefaultLockDurationDays;
    public int ResendCooldownMinutes { get; set; } = DefaultResendCooldownMinutes;
    public int ResendRateLimitPerHour { get; set; } = DefaultResendRateLimitPerHour;

    /// <summary>Проверяет значения на старте понятной ошибкой без секретов.</summary>
    public void Validate()
    {
        RequirePositive(nameof(MaxAttempts), MaxAttempts);
        RequirePositive(nameof(LockDurationDays), LockDurationDays);
        if (ResendCooldownMinutes < 0)
            throw new InvalidOperationException(
                "Конфигурация: AuthCode:ResendCooldownMinutes не может быть отрицательным.");
        RequirePositive(nameof(ResendRateLimitPerHour), ResendRateLimitPerHour);
    }

    private static void RequirePositive(string name, int value)
    {
        if (value <= 0)
            throw new InvalidOperationException(
                $"Конфигурация: AuthCode:{name} должен быть положительным.");
    }
}
