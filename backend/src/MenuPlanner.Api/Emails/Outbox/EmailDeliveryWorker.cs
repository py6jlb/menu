using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MenuPlanner.Api.Emails.Outbox;

/// <summary>
/// Фоновая отправка: периодически забирает принятые письма из очереди и доводит
/// их с ограниченными повторами. Записи живут в PostgreSQL, поэтому переживают
/// перезапуск; захват строк не даёт параллельным воркерам обработать одну запись
/// дважды.
/// </summary>
public sealed class EmailDeliveryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly EmailOutboxOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<EmailDeliveryWorker> _logger;

    public EmailDeliveryWorker(
        IServiceScopeFactory scopes,
        EmailOutboxOptions options,
        TimeProvider clock,
        ILogger<EmailDeliveryWorker> logger)
    {
        _scopes = scopes;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<EmailOutboxProcessor>();
                await processor.DispatchDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Фоновая отправка писем: проход завершился ошибкой.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.PollSeconds), _clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
