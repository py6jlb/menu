using Microsoft.Extensions.Logging;

namespace MenuPlanner.Api.Emails;

/// <summary>
/// Безопасная диагностика и пользовательский текст при сбое доставки.
/// В журнал идут только причина и тип сбоя — без адреса, кода, тела письма
/// и учётных данных.
/// </summary>
public static class EmailDeliveryFailure
{
    public const string UserMessage = "Не удалось отправить письмо. Попробуйте позже.";

    public static void LogSafe(ILogger logger, EmailDeliveryException failure) =>
        logger.LogError(
            failure,
            "Доставка письма не выполнена: {Reason} ({FailureType})",
            failure.Reason,
            failure.FailureType);
}
