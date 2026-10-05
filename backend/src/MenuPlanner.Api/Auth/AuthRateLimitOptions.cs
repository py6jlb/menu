namespace MenuPlanner.Api.Auth;

/// <summary>
/// Значения лимитов для аутентификационных операций. Читаются из секции
/// <c>AuthRateLimit</c> и переопределяются env-переменными; проверяются на
/// старте понятной ошибкой без раскрытия секретов. Лимиты действуют на одну
/// реплику (локальный store), см. ADR-0010.
/// </summary>
public sealed class AuthRateLimitOptions
{
    public const string SectionName = "AuthRateLimit";

    public const int DefaultLoginPerIpPerMinute = 30;
    public const int DefaultLoginPerEmailPerMinute = 10;
    public const int DefaultRegisterPerIpPerHour = 10;
    public const int DefaultRegisterPerEmailPerHour = 5;
    public const int DefaultResetPerIpPerHour = 10;
    public const int DefaultResetPerEmailPerHour = 10;
    public const int DefaultUnlockPerAdminPerMinute = 10;
    public const int DefaultUnlockPerIpPerMinute = 30;
    public const int DefaultMaxTrackedKeys = FixedWindowRateLimiter.DefaultMaxKeys;

    public int LoginPerIpPerMinute { get; set; } = DefaultLoginPerIpPerMinute;
    public int LoginPerEmailPerMinute { get; set; } = DefaultLoginPerEmailPerMinute;
    public int RegisterPerIpPerHour { get; set; } = DefaultRegisterPerIpPerHour;
    public int RegisterPerEmailPerHour { get; set; } = DefaultRegisterPerEmailPerHour;
    public int ResetPerIpPerHour { get; set; } = DefaultResetPerIpPerHour;
    public int ResetPerEmailPerHour { get; set; } = DefaultResetPerEmailPerHour;
    public int UnlockPerAdminPerMinute { get; set; } = DefaultUnlockPerAdminPerMinute;
    public int UnlockPerIpPerMinute { get; set; } = DefaultUnlockPerIpPerMinute;
    public int MaxTrackedKeys { get; set; } = DefaultMaxTrackedKeys;

    /// <summary>Читает секцию, накладывает env-переменные и валидирует значения.</summary>
    public static AuthRateLimitOptions Read(IConfiguration configuration)
    {
        var options = configuration.GetSection(SectionName).Get<AuthRateLimitOptions>()
            ?? new AuthRateLimitOptions();

        ApplyInt(configuration, "AUTH_RATE_LIMIT_LOGIN_PER_IP_PER_MINUTE", v => options.LoginPerIpPerMinute = v);
        ApplyInt(configuration, "AUTH_RATE_LIMIT_LOGIN_PER_EMAIL_PER_MINUTE", v => options.LoginPerEmailPerMinute = v);
        ApplyInt(configuration, "AUTH_RATE_LIMIT_REGISTER_PER_IP_PER_HOUR", v => options.RegisterPerIpPerHour = v);
        ApplyInt(configuration, "AUTH_RATE_LIMIT_REGISTER_PER_EMAIL_PER_HOUR", v => options.RegisterPerEmailPerHour = v);
        ApplyInt(configuration, "AUTH_RATE_LIMIT_RESET_PER_IP_PER_HOUR", v => options.ResetPerIpPerHour = v);
        ApplyInt(configuration, "AUTH_RATE_LIMIT_RESET_PER_EMAIL_PER_HOUR", v => options.ResetPerEmailPerHour = v);
        ApplyInt(configuration, "AUTH_RATE_LIMIT_UNLOCK_PER_ADMIN_PER_MINUTE", v => options.UnlockPerAdminPerMinute = v);
        ApplyInt(configuration, "AUTH_RATE_LIMIT_UNLOCK_PER_IP_PER_MINUTE", v => options.UnlockPerIpPerMinute = v);
        ApplyInt(configuration, "AUTH_RATE_LIMIT_MAX_TRACKED_KEYS", v => options.MaxTrackedKeys = v);

        options.Validate();
        return options;
    }

    public void Validate()
    {
        RequirePositive(nameof(LoginPerIpPerMinute), LoginPerIpPerMinute);
        RequirePositive(nameof(LoginPerEmailPerMinute), LoginPerEmailPerMinute);
        RequirePositive(nameof(RegisterPerIpPerHour), RegisterPerIpPerHour);
        RequirePositive(nameof(RegisterPerEmailPerHour), RegisterPerEmailPerHour);
        RequirePositive(nameof(ResetPerIpPerHour), ResetPerIpPerHour);
        RequirePositive(nameof(ResetPerEmailPerHour), ResetPerEmailPerHour);
        RequirePositive(nameof(UnlockPerAdminPerMinute), UnlockPerAdminPerMinute);
        RequirePositive(nameof(UnlockPerIpPerMinute), UnlockPerIpPerMinute);
        RequirePositive(nameof(MaxTrackedKeys), MaxTrackedKeys);
    }

    private static void ApplyInt(IConfiguration configuration, string key, Action<int> apply)
    {
        var raw = configuration[key];
        if (string.IsNullOrWhiteSpace(raw))
            return;

        if (!int.TryParse(raw, out var value))
            throw new InvalidOperationException(
                $"Конфигурация: {key} должен быть целым числом.");

        apply(value);
    }

    private static void RequirePositive(string name, int value)
    {
        if (value <= 0)
            throw new InvalidOperationException(
                $"Конфигурация: AuthRateLimit:{name} должен быть положительным.");
    }
}
