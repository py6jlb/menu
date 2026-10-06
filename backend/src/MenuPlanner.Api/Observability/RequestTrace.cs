using System.Diagnostics;

namespace MenuPlanner.Api.Observability;

/// <summary>
/// Единый идентификатор запроса для корреляции: W3C trace-id активного Activity,
/// иначе — идентификатор запроса ASP.NET Core. Одна реализация для HTTP-ответа и
/// журнальных записей, поэтому клиентский trace-id находится по журналу.
/// </summary>
public static class RequestTrace
{
    public static string Current(HttpContext context) =>
        Activity.Current?.TraceId.ToHexString() ?? context.TraceIdentifier;
}
