using System.Text.RegularExpressions;

namespace MenuPlanner.Api.Configuration;

/// <summary>
/// Проверка production-конфигурации до публичной работы с реальными
/// пользователями. Production отклоняет HTTP-адрес, отсутствие почты и
/// известные placeholder-секреты понятной ошибкой без раскрытия значений.
/// Лабораторный режим (<c>DEPLOYMENT_MODE=lab</c>) включается явно.
/// </summary>
public static partial class ProductionConfiguration
{
    private const int MinJwtSecretLength = 32;
    private const int MinDbPasswordLength = 12;

    // Признаки шаблонного секрета. Делятся по не-алфавитно-цифровым
    // разделителям: случайный секрет — один длинный токен и не совпадает.
    private static readonly HashSet<string> PlaceholderTokens = new(StringComparer.Ordinal)
    {
        "dev", "development", "only", "secret", "change", "changeme", "placeholder",
        "example", "password", "passwd", "test", "dummy", "sample", "default",
        "todo", "insecure", "local", "demo"
    };

    // Точные известные шаблонные значения (dev-defaults из appsettings/compose).
    private static readonly HashSet<string> KnownPlaceholderSecrets = new(StringComparer.Ordinal)
    {
        "dev-only-secret-change-me-in-production-0123456789abcdef",
        "change-me-min-32-chars-0123456789abcdef",
        "change-me-strong",
        "changeme",
        "menu",
        "postgres",
        "password",
        "secret"
    };

    public static DeploymentOptions Read(IConfiguration configuration, string jwtSecret)
    {
        var mode = (configuration["DEPLOYMENT_MODE"] ?? "").Trim();
        if (mode.Length == 0) mode = DeploymentOptions.ProductionMode;
        mode = mode.ToLowerInvariant();

        if (mode is not (DeploymentOptions.ProductionMode or DeploymentOptions.LabMode))
            throw new InvalidOperationException(
                "Конфигурация: DEPLOYMENT_MODE должен быть 'production' или 'lab'");

        if (mode == DeploymentOptions.LabMode)
            return new DeploymentOptions { Mode = mode };

        var publicBaseUrl = (configuration["PUBLIC_BASE_URL"] ?? "").Trim();
        if (!IsHttpsUrl(publicBaseUrl))
            throw new InvalidOperationException(
                "Конфигурация: production требует PUBLIC_BASE_URL вида https://<домен>; "
                + "лабораторный HTTP включается явно через DEPLOYMENT_MODE=lab");

        var smtpHost = (configuration["SMTP_HOST"] ?? "").Trim();
        var smtpFrom = (configuration["SMTP_FROM"] ?? "").Trim();
        if (smtpHost.Length == 0 || smtpFrom.Length == 0)
            throw new InvalidOperationException(
                "Конфигурация: production требует непустых SMTP_HOST и SMTP_FROM "
                + "(режим без писем — только DEPLOYMENT_MODE=lab)");

        RequireStrongSecret("JWT_SECRET", jwtSecret, MinJwtSecretLength);

        if (configuration["DB_HOST"] is not null)
            RequireStrongSecret("DB_PASSWORD", configuration["DB_PASSWORD"], MinDbPasswordLength);

        var smtpPassword = configuration["SMTP_PASSWORD"];
        if (!string.IsNullOrEmpty(smtpPassword))
            RequireStrongSecret("SMTP_PASSWORD", smtpPassword, minLength: 0);

        return new DeploymentOptions { Mode = mode };
    }

    /// <summary>Известное шаблонное/dev-значение (без раскрытия самого значения).</summary>
    public static bool IsPlaceholderSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var normalized = value.Trim().ToLowerInvariant();
        if (KnownPlaceholderSecrets.Contains(normalized)) return true;

        foreach (Match match in WordRegex().Matches(normalized))
        {
            if (PlaceholderTokens.Contains(match.Value)) return true;
        }
        return false;
    }

    private static void RequireStrongSecret(string key, string? value, int minLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Конфигурация: production требует непустой {key}");

        if (minLength > 0 && value.Trim().Length < minLength)
            throw new InvalidOperationException(
                $"Конфигурация: {key} должен быть не короче {minLength} символов");

        if (IsPlaceholderSecret(value))
            throw new InvalidOperationException(
                $"Конфигурация: {key} выглядит как известное шаблонное значение; "
                + "задай случайный секрет");
    }

    private static bool IsHttpsUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && !string.IsNullOrEmpty(uri.Host);

    [GeneratedRegex("[a-z0-9]+")]
    private static partial Regex WordRegex();
}
