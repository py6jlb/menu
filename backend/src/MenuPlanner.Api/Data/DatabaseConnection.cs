using Microsoft.Extensions.Configuration;

namespace MenuPlanner.Api.Data;

/// <summary>
/// Единый резолвер строки подключения к БД для рантайма (<see cref="Program"/>) и
/// design-time фабрики (<see cref="AppDbContextFactory"/>): конфиг/переменные окружения,
/// затем dev-fallback на localhost.
/// </summary>
public static class DatabaseConnection
{
    public const string DevFallback =
        "Host=localhost;Port=5432;Database=menu_planner;Username=menu;Password=menu";

    public static string Resolve(IConfiguration configuration) =>
        configuration.GetConnectionString("Default")
        ?? configuration["DB_CONNECTION_STRING"]
        ?? DevFallback;
}
