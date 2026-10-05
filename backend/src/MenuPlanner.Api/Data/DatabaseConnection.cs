using System.Globalization;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace MenuPlanner.Api.Data;

/// <summary>
/// Единый резолвер строки подключения к БД для рантайма (<see cref="Program"/>) и
/// design-time фабрики (<see cref="AppDbContextFactory"/>): structured-конфигурация
/// при заданном DB_HOST, иначе прежние строки подключения и dev-fallback.
/// </summary>
public static class DatabaseConnection
{
    public const string DevFallback =
        "Host=localhost;Port=5432;Database=menu_planner;Username=menu;Password=menu";

    public static string Resolve(IConfiguration configuration)
    {
        if (configuration["DB_HOST"] is { } host)
        {
            var port = 5432;
            if (configuration["DB_PORT"] is { } configuredPort
                && (!int.TryParse(configuredPort, NumberStyles.None, CultureInfo.InvariantCulture, out port)
                    || port is < 1 or > 65535))
            {
                throw new InvalidOperationException("Конфигурация: DB_PORT должен быть целым от 1 до 65535");
            }

            return new NpgsqlConnectionStringBuilder
            {
                Host = host,
                Port = port,
                Database = configuration["DB_NAME"] ?? "menu_planner",
                Username = configuration["DB_USER"] ?? "menu",
                Password = configuration["DB_PASSWORD"] ?? "menu"
            }.ConnectionString;
        }

        return configuration.GetConnectionString("Default")
            ?? configuration["DB_CONNECTION_STRING"]
            ?? DevFallback;
    }
}
