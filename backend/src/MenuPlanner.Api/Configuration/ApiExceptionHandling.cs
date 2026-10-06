using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MenuPlanner.Api.Configuration;

/// <summary>
/// Единый ответ на неожиданный сбой: клиент получает машинный код и trace-id для
/// корреляции, но не stack trace, SQL или секреты. Malformed payload отделяется от
/// внутренней ошибки: битый запрос — 400, непредвиденный сбой — 500.
/// </summary>
public static class ApiExceptionHandling
{
    public static IApplicationBuilder UseApiExceptionHandling(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            catch (BadHttpRequestException exception) when (!context.Response.HasStarted)
            {
                var logger = context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("ApiExceptionHandling");
                logger.LogWarning(
                    exception,
                    "Некорректный запрос {Method} {Path}. TraceId={TraceId}",
                    context.Request.Method,
                    context.Request.Path,
                    TraceId(context));

                await WriteAsync(context, StatusCodes.Status400BadRequest,
                    "Некорректный запрос.", "malformed_request");
            }
            catch (Exception exception) when (!context.Response.HasStarted)
            {
                var logger = context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("ApiExceptionHandling");
                logger.LogError(
                    exception,
                    "Необработанная ошибка {Method} {Path}. TraceId={TraceId}",
                    context.Request.Method,
                    context.Request.Path,
                    TraceId(context));

                await WriteAsync(context, StatusCodes.Status500InternalServerError,
                    "Внутренняя ошибка сервера.", "internal_error");
            }
        });

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
