namespace MenuPlanner.Api.Domain;

/// <summary>
/// Состояние записи очереди писем. <see cref="Pending"/> — ждёт отправки (в том
/// числе после временного отказа), <see cref="InProgress"/> — захвачена
/// отправляющим воркером, <see cref="Failed"/> — окончательный отказ либо
/// потерявший актуальность challenge.
/// </summary>
public enum EmailOutboxStatus
{
    Pending,
    InProgress,
    Failed
}

/// <summary>
/// Принятая к отправке доставка письма (маленький outbox без брокера).
/// Запись создаётся в одной транзакции с изменением аккаунта/challenge, поэтому
/// временная недоступность SMTP не теряет письмо: его доводит фоновый воркер.
///
/// Содержимое письма (одноразовый код) хранится в защищённом виде в
/// <see cref="ProtectedPayload"/> и никогда не логируется. Запись удаляется после
/// успешной отправки; окончательно проваленные записи удаляются по истечении
/// срока хранения.
/// </summary>
public sealed class EmailOutboxMessage
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Challenge, к которому относится письмо: повтор не создаёт новый код.</summary>
    public Guid AuthCodeId { get; set; }
    public AuthCode? AuthCode { get; set; }

    /// <summary>Тип письма/кода: подтверждение почты или сброс пароля.</summary>
    public AuthCodeType Type { get; set; }

    public required string Recipient { get; set; }

    /// <summary>Зашифрованный код письма. Плейнтекст в БД и журналы не попадает.</summary>
    public required string ProtectedPayload { get; set; }

    public EmailOutboxStatus Status { get; set; } = EmailOutboxStatus.Pending;

    public int Attempts { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime NextAttemptAt { get; set; }

    public DateTime? ClaimedAt { get; set; }

    /// <summary>Токен захвата: воркер обрабатывает только свои строки.</summary>
    public Guid? ClaimToken { get; set; }

    /// <summary>Безопасная причина последнего отказа (код перечисления, не текст).</summary>
    public string? LastFailureReason { get; set; }
}
