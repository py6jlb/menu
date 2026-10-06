using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MenuPlanner.Api.Emails.Outbox;

/// <summary>
/// Быстрый путь после фиксации транзакции: сразу запускает отправку в отдельной
/// области, не задерживая ответ и не разделяя область с вызывающим. Сбой
/// отправки не пробрасывается: письмо уже надёжно сохранено в очереди, и воркер
/// доведёт его позже.
/// </summary>
public sealed class EmailDispatchTrigger
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<EmailDispatchTrigger> _logger;

    public EmailDispatchTrigger(IServiceScopeFactory scopes, ILogger<EmailDispatchTrigger> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public async Task TryDispatchAsync(CancellationToken ct = default)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<EmailOutboxProcessor>();
            await processor.DispatchDueAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Немедленная отправка писем не удалась; письма останутся в очереди.");
        }
    }
}
