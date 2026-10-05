using Npgsql;

namespace MenuPlanner.Api.Tests.Postgres;

/// <summary>
/// Точка включения настоящих PostgreSQL-проверок. Быстрый suite (EF InMemory)
/// не знает об этом классе; PostgreSQL-классы пропускаются, пока не задана
/// строка подключения к изолированному серверу.
/// </summary>
public static class PostgresTestEnvironment
{
    public const string VariableName = "MENU_PLANNER_TEST_POSTGRES";

    public static string? ConnectionString =>
        Environment.GetEnvironmentVariable(VariableName);

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);

    /// <summary>Строка подключения к служебной БД <c>postgres</c> для CREATE/DROP DATABASE.</summary>
    public static string AdminConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionString);
        builder.Database = "postgres";
        return builder.ConnectionString;
    }
}
