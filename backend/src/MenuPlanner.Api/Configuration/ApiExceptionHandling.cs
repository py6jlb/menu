using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MenuPlanner.Api.Observability;

namespace MenuPlanner.Api.Configuration;

/// <summary>
/// Единый ответ на неожиданный сбой: клиент получает машинный код и trace-id для
/// корреляции, но не stack trace, SQL или секреты. Malformed payload отделяется от
/// внутренней ошибки: битый запрос — 400, непредвиденный сбой — 500.
/// Зависимости приходят через конструктор middleware, без service locator.
/// </summary>
public sealed class ApiExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionHandlingMiddleware> _logger;

    public ApiExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ApiExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (BadHttpRequestException exception) when (!context.Response.HasStarted)
        {
            _logger.LogWarning(
                exception,
                "Некорректный запрос {Method} {Path}. TraceId={TraceId}",
                context.Request.Method,
                SensitivePath.Minimize(context.Request.Path),
                TraceId(context));

            await WriteAsync(context, StatusCodes.Status400BadRequest,
                "Некорректный запрос.", "malformed_request");
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            _logger.LogError(
                exception,
                "Необработанная ошибка {Method} {Path}. TraceId={TraceId}",
                context.Request.Method,
                SensitivePath.Minimize(context.Request.Path),
                TraceId(context));

            await WriteAsync(context, StatusCodes.Status500InternalServerError,
                "Внутренняя ошибка сервера.", "internal_error");
        }
    }

    private static async Task WriteAsync(HttpContext context, int statusCode, string message, string code)
    {
        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(
            new ApiErrorDto(message, code, TraceId(context)),
            context.RequestAborted);
    }

    private static string TraceId(HttpContext context) =>
        Activity.Current?.Id ?? context.TraceIdentifier;
}

/// <summary>Непредвиденный сбой: русский текст, машинный код и trace-id запроса.</summary>
public sealed record ApiErrorDto(string Error, string Code, string? TraceId = null);

/// <summary>Подключение обработчика непредвиденных сбоев к конвейеру.</summary>
public static class ApiExceptionHandlingExtensions
{
    public static IApplicationBuilder UseApiExceptionHandling(this IApplicationBuilder app) =>
        app.UseMiddleware<ApiExceptionHandlingMiddleware>();
}
