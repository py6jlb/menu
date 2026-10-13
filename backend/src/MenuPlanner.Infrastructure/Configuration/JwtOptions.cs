namespace MenuPlanner.Infrastructure.Configuration;

/// <summary>Настройки JWT из секции Jwt: издатель, аудитория, секрет и срок жизни.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "menu-planner";
    public string Audience { get; set; } = "menu-planner-client";
    public string Secret { get; set; } = "";
    public int ExpiryMinutes { get; set; } = 60;

    /// <summary>
    /// Читает секцию Jwt и env-переопределения. Секрет короче 32 символов
    /// отклоняется на старте понятной ошибкой (значение не раскрывается).
    /// </summary>
    public static JwtOptions Read(IConfiguration configuration)
    {
        var options = configuration.GetSection(SectionName).Get<JwtOptions>()
            ?? new JwtOptions();

        if (configuration["JWT_SECRET"] is { Length: > 0 } secret) options.Secret = secret;
        if (configuration["JWT_ISSUER"] is { Length: > 0 } issuer) options.Issuer = issuer;
        if (configuration["JWT_AUDIENCE"] is { Length: > 0 } audience) options.Audience = audience;
        if (int.TryParse(configuration["JWT_EXPIRY_MINUTES"], out var minutes)) options.ExpiryMinutes = minutes;

        if (options.Secret.Length < 32)
            throw new InvalidOperationException(
                "Jwt:Secret (или env JWT_SECRET) должен быть не короче 32 символов.");

        return options;
    }
}
