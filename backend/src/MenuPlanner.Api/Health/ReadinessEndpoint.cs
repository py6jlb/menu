namespace MenuPlanner.Api.Health;

/// <summary>
/// Дешёвый liveness остаётся на <c>/health</c>. Readiness на <c>/ready</c>
/// проверяет БД с ограниченным ожиданием и отвечает 503, не раскрывая
/// секретов (ни строки подключения, ни текста исключения).
/// </summary>
public static class ReadinessEndpoint
{
    public const string ServiceName = "menu-planner-api";

    public static async Task<IResult> GetReadinessAsync(
        IDatabaseReadinessProbe probe,
        ReadinessOptions options,
        ILoggerFactory loggerFactory,
        CancellationToken requestToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestToken);
        timeout.CancelAfter(options.Timeout);

        var ready = false;
        try
        {
            ready = await probe.CanConnectAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            ready = false;
        }
        catch (Exception exception)
        {
            loggerFactory
                .CreateLogger("MenuPlanner.Api.Health.Readiness")
                .LogWarning(
                    "Readiness: проверка БД завершилась ошибкой {Error}.",
                    exception.GetType().Name);
            ready = false;
        }

        return ready
            ? Results.Json(new ReadinessResponse("ready", ServiceName))
            : Results.Json(
                new ReadinessResponse("not-ready", ServiceName),
                statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private sealed record ReadinessResponse(string Status, string Service);
}
