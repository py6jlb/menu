using MenuPlanner.Api.Features.Auth.Codes;
using MenuPlanner.Domain.Accounts;

namespace MenuPlanner.Api.Features.Auth.Codes;

/// <summary>Настройки одноразовых кодов из секции AuthCode: лимиты попыток, блокировки и повторной отправки.</summary>
public sealed class AuthCodeOptions
{
    public const string SectionName = "AuthCode";

    public const int DefaultMaxAttempts = AuthCodePolicy.MaxAttempts;
    public const int DefaultLockDurationDays = 3;
    public const int DefaultResendCooldownMinutes = 5;
    public const int DefaultResendRateLimitPerHour = 5;

    public int MaxAttempts { get; set; } = DefaultMaxAttempts;
    public int LockDurationDays { get; set; } = DefaultLockDurationDays;
    public int ResendCooldownMinutes { get; set; } = DefaultResendCooldownMinutes;
    public int ResendRateLimitPerHour { get; set; } = DefaultResendRateLimitPerHour;

    /// <summary>Читает секцию AuthCode и env-переопределения, проверяя значения на старте.</summary>
    public static AuthCodeOptions Read(IConfiguration configuration)
    {
        var options = configuration.GetSection(SectionName).Get<AuthCodeOptions>()
            ?? new AuthCodeOptions();

        if (int.TryParse(configuration["AUTH_CODE_MAX_ATTEMPTS"], out var maxAttempts))
            options.MaxAttempts = maxAttempts;
        if (int.TryParse(configuration["AUTH_CODE_LOCK_DAYS"], out var lockDays))
            options.LockDurationDays = lockDays;
        if (int.TryParse(configuration["AUTH_CODE_RESEND_COOLDOWN_MINUTES"], out var cooldownMinutes))
            options.ResendCooldownMinutes = cooldownMinutes;
        if (int.TryParse(configuration["AUTH_CODE_RESEND_RATE_LIMIT_PER_HOUR"], out var rateLimit))
            options.ResendRateLimitPerHour = rateLimit;

        options.Validate();
        return options;
    }

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
