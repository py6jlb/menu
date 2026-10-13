using Microsoft.Extensions.Configuration;

namespace MenuPlanner.Api.Emails;

/// <summary>
/// Чтение и проверка SMTP-настроек. Защита обязательна: открытого текста и
/// отклонения проверки сертификата нет, несовместимые host/port/TLS и
/// отсутствующие поля дают понятную ошибку без секретов.
/// </summary>
public static class EmailConfiguration
{
    public const string TransportVariable = "EMAIL_TRANSPORT";

    public static EmailOptions Read(IConfiguration configuration)
    {
        var options = new EmailOptions();

        if (configuration["SMTP_HOST"] is { Length: > 0 } host) options.Host = host.Trim();
        if (configuration["SMTP_PORT"] is { Length: > 0 } port)
        {
            if (!int.TryParse(port.Trim(), out var portValue))
                throw new InvalidOperationException(
                    "Конфигурация: SMTP_PORT должен быть целым числом 1–65535");
            options.Port = portValue;
        }
        if (configuration["SMTP_USER"] is { Length: > 0 } user) options.User = user;
        if (configuration["SMTP_PASSWORD"] is { Length: > 0 } password) options.Password = password;
        if (configuration["SMTP_FROM"] is { Length: > 0 } from) options.From = from.Trim();
        if (configuration["SMTP_FROM_NAME"] is { Length: > 0 } fromName) options.FromName = fromName;
        if (configuration["SMTP_TIMEOUT_SECONDS"] is { Length: > 0 } timeout)
        {
            if (!int.TryParse(timeout.Trim(), out var seconds))
                throw new InvalidOperationException(
                    "Конфигурация: SMTP_TIMEOUT_SECONDS должен быть целым числом 1–300");
            options.TimeoutSeconds = seconds;
        }

        ReadSecurity(configuration, options);
        ReadTransport(configuration, options);
        Validate(options);

        return options;
    }

    private static void ReadSecurity(IConfiguration configuration, EmailOptions options)
    {
        // Устаревший ключ раньше умел выключать TLS. Теперь это запрещено явно.
        if (configuration["SMTP_ENABLE_STARTTLS"] is { Length: > 0 } legacy &&
            bool.TryParse(legacy.Trim(), out var enable) && !enable)
            throw new InvalidOperationException(
                "Конфигурация: SMTP_ENABLE_STARTTLS=false больше не поддерживается — "
                + "защита SMTP обязательна, задай SMTP_SECURITY=starttls или ssl");

        if (configuration["SMTP_SECURITY"] is not { Length: > 0 } security) return;
        options.Security = security.Trim().ToLowerInvariant() switch
        {
            "starttls" => SmtpSecurity.StartTls,
            "ssl" or "tls" => SmtpSecurity.Ssl,
            _ => throw new InvalidOperationException(
                "Конфигурация: SMTP_SECURITY должен быть starttls или ssl")
        };
    }

    private static void ReadTransport(IConfiguration configuration, EmailOptions options)
    {
        var value = configuration[TransportVariable]?.Trim().ToLowerInvariant();
        options.Transport = value switch
        {
            null or "" => string.IsNullOrWhiteSpace(options.Host)
                ? EmailTransportMode.Log
                : EmailTransportMode.Smtp,
            "smtp" => EmailTransportMode.Smtp,
            "log" => EmailTransportMode.Log,
            _ => throw new InvalidOperationException(
                "Конфигурация: EMAIL_TRANSPORT должен быть smtp или log")
        };
    }

    private static void Validate(EmailOptions options)
    {
        if (options.Port is < 1 or > 65535)
            throw new InvalidOperationException(
                "Конфигурация: SMTP_PORT должен быть целым числом 1–65535");

        if (options.TimeoutSeconds is < 1 or > 300)
            throw new InvalidOperationException(
                "Конфигурация: SMTP_TIMEOUT_SECONDS должен быть целым числом 1–300");

        if (string.IsNullOrWhiteSpace(options.Host))
        {
            if (options.Transport != EmailTransportMode.Log)
                throw new InvalidOperationException(
                    "Конфигурация: SMTP_HOST не задан; для dev/test явно выбери "
                    + "EMAIL_TRANSPORT=log (доступно только в режиме lab)");
            return;
        }

        if (string.IsNullOrWhiteSpace(options.From))
            throw new InvalidOperationException(
                "Конфигурация: SMTP_FROM обязателен при заданном SMTP_HOST");

        if (options.User.Length == 0 && options.Password.Length > 0)
            throw new InvalidOperationException(
                "Конфигурация: SMTP_PASSWORD задан, но SMTP_USER пуст");

        if (options.User.Length > 0 && options.Password.Length == 0)
            throw new InvalidOperationException(
                "Конфигурация: SMTP_USER задан, но SMTP_PASSWORD пуст");

        if (options.Security == SmtpSecurity.StartTls && options.Port == 465)
            throw new InvalidOperationException(
                "Конфигурация: SMTP_PORT=465 подразумевает неявный TLS — задай SMTP_SECURITY=ssl");

        if (options.Security == SmtpSecurity.Ssl && options.Port == 587)
            throw new InvalidOperationException(
                "Конфигурация: SMTP_PORT=587 подразумевает STARTTLS — задай SMTP_SECURITY=starttls");
    }
}
