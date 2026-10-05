using Microsoft.AspNetCore.Http;

namespace MenuPlanner.Api.Auth;

/// <summary>
/// Клиентский IP после доверенного proxy middleware. Заголовки
/// <c>X-Forwarded-For</c> напрямую не читаются: <c>UseForwardedHeaders</c>
/// подменяет <see cref="HttpContext.Connection"/>.RemoteIpAddress только если
/// непосредственный прокси входит в доверенный список; иначе остаётся адрес
/// прямого peer, и поддельные заголовки игнорируются.
/// </summary>
public static class ClientIpResolver
{
    public static string Resolve(HttpContext http) =>
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
