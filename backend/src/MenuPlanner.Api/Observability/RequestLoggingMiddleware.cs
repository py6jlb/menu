using System.Diagnostics;
using Microsoft.AspNetCore.Routing;

namespace MenuPlanner.Api.Observability;

/// <summary>
/// Одна структурированная запись на запрос: operation (шаблон маршрута), статус,
/// длительность, trace-id и release-id. Логируется после обработки, поэтому в
/// журнал попадают и успешные, и ошибочные ответы. Секретные share-токены в пути
/// минимизируются, тело запроса не пишется.
/// </summary>
public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;
    private readonly ObservabilityOptions _options;

    public RequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestLoggingMiddleware> logger,
        ObservabilityOptions options)
    {
        _next = next;
        _logger = logger;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // trace-id захватывается до вызова конвейера: после завершения Activity
        // перестаёт быть текущей.
        var traceId = Activity.Current?.TraceId.ToHexString() ?? context.TraceIdentifier;
        var started = Stopwatch.GetTimestamp();
        try
        {
            await _next(context);
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var routePattern = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
            _logger.LogInformation(
                "HTTP {Operation} -> {StatusCode} за {ElapsedMs:0.0} мс. TraceId={TraceId} ReleaseId={ReleaseId}",
                SensitivePath.ForLogging(routePattern, context.Request.Path),
                context.Response.StatusCode,
                elapsedMs,
                traceId,
                _options.Release.Id);
        }
    }
}

/// <summary>Подключение журналирования запросов к конвейеру.</summary>
public static class RequestLoggingExtensions
{
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app) =>
        app.UseMiddleware<RequestLoggingMiddleware>();
}
