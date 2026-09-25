using Microsoft.AspNetCore.Http;

namespace MenuPlanner.Api.Auth;

public static class ClientIpResolver
{
    public static string Resolve(HttpContext http)
    {
        var forwarded = http.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(first))
                return first;
        }

        return http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
