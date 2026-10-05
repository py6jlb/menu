using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace MenuPlanner.Api.Configuration;

/// <summary>
/// Сборка параметров доверенных прокси. X-Forwarded-* принимаются только от
/// перечисленных адресов/сетей: край (Caddy) в prod и nginx во время dev.
/// Значения задаются env-переменными <c>TRUSTED_PROXY_ADDRESSES</c> и
/// <c>TRUSTED_PROXY_NETWORKS</c>. Если ничего не задано, middleware доверенных
/// заголовков не подключается (см. <see cref="HasTrustedProxies"/>): пустой
/// известный список ASP.NET Core трактует как «доверять всем».
/// </summary>
public static class ForwardedHeaderConfiguration
{
    /// <summary>Есть ли хотя бы один доверенный адрес/сеть.</summary>
    public static bool HasTrustedProxies(ForwardedHeadersOptions options) =>
        options.KnownProxies.Count > 0 || options.KnownIPNetworks.Count > 0;

    public static ForwardedHeadersOptions Build(IConfiguration configuration)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1
        };
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();

        foreach (var value in SplitList(configuration["TRUSTED_PROXY_ADDRESSES"]))
        {
            if (!IPAddress.TryParse(value, out var address))
                throw new InvalidOperationException(
                    $"Конфигурация: TRUSTED_PROXY_ADDRESSES содержит некорректный адрес '{value}'.");
            options.KnownProxies.Add(address);
        }

        foreach (var value in SplitList(configuration["TRUSTED_PROXY_NETWORKS"]))
        {
            var parts = value.Split('/', 2);
            if (parts.Length != 2
                || !IPAddress.TryParse(parts[0], out var prefix)
                || !int.TryParse(parts[1], out var length))
            {
                throw new InvalidOperationException(
                    $"Конфигурация: TRUSTED_PROXY_NETWORKS содержит некорректную сеть '{value}' (ожидается CIDR).");
            }

            try
            {
                options.KnownIPNetworks.Add(new System.Net.IPNetwork(prefix, length));
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new InvalidOperationException(
                    $"Конфигурация: TRUSTED_PROXY_NETWORKS содержит некорректную длину префикса '{value}'.");
            }
        }

        return options;
    }

    private static IEnumerable<string> SplitList(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? Array.Empty<string>()
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
