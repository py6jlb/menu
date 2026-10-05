namespace MenuPlanner.Api.Emails;

/// <summary>Куда уходит письмо: защищённый SMTP или запись в лог (только dev/test).</summary>
public enum EmailTransportMode
{
    Smtp,
    Log
}

/// <summary>Обязательный способ защиты SMTP-соединения. Открытого текста нет.</summary>
public enum SmtpSecurity
{
    /// <summary>Явно требуемый STARTTLS (типично порт 587).</summary>
    StartTls,

    /// <summary>Неявный TLS с первого байта (типично порт 465).</summary>
    Ssl
}

public sealed class EmailOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
    public string FromName { get; set; } = "Меню для домохозяек";
    public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;

    /// <summary>Предел ожидания SMTP-операций; ошибка не считается доставкой.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    public EmailTransportMode Transport { get; set; } = EmailTransportMode.Smtp;
}
