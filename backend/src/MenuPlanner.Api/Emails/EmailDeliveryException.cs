namespace MenuPlanner.Api.Emails;

/// <summary>
/// Причина сбоя транспорта. Подставляется в понятный ответ и безопасный лог
/// без адреса, кода, тела письма и учётных данных.
/// </summary>
public enum EmailFailureReason
{
    Timeout,
    TlsUnavailable,
    Certificate,
    Authentication,
    Connection,
    Protocol,
    Unknown
}

/// <summary>
/// Сбой SMTP-доставки. Несёт только обобщённое сообщение, причину и имя типа
/// исходного исключения: исходный <see cref="Exception"/> не вкладывается,
/// чтобы его текст (SMTP-диалог, учётные данные) не попал в журналы.
/// </summary>
public sealed class EmailDeliveryException : Exception
{
    public EmailDeliveryException(EmailFailureReason reason, string failureType)
        : base($"Не удалось отправить письмо по защищённому SMTP ({reason}).")
    {
        Reason = reason;
        FailureType = failureType;
    }

    public EmailFailureReason Reason { get; }

    public string FailureType { get; }
}
