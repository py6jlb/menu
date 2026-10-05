using MenuPlanner.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MenuPlanner.Api.Tests.Postgres;

/// <summary>
/// Одноразовая база на изолированном PostgreSQL-сервере. Создаётся под
/// уникальным именем, поэтому не пересекается ни с dev-БД, ни с рабочими
/// данными; удаляется с FORCE даже если тест упал.
/// </summary>
public sealed class PostgresDatabase : IAsyncDisposable
{
    private readonly string _adminConnectionString;
    private readonly string _databaseName;
    private bool _disposed;

    public string ConnectionString { get; }

    private PostgresDatabase(string adminConnectionString, string databaseName)
    {
        _adminConnectionString = adminConnectionString;
        _databaseName = databaseName;
        ConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Database = databaseName
        }.ConnectionString;
    }

    public static async Task<PostgresDatabase> CreateAsync()
    {
        var adminConnectionString = PostgresTestEnvironment.AdminConnectionString();
        var databaseName = "menu_planner_pgt_" + Guid.NewGuid().ToString("N");

        await using (var connection = new NpgsqlConnection(adminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
            await command.ExecuteNonQueryAsync();
        }

        return new PostgresDatabase(adminConnectionString, databaseName);
    }

    public DbContextOptions<AppDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

    public AppDbContext CreateContext() => new(CreateOptions());

    /// <summary>Снимает БД, эмулируя недоступность базы для readiness.</summary>
    public async Task DropAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(ConnectionString));
        await ExecuteAdminAsync($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)");
    }

    /// <summary>Возвращает БД под тем же именем — готовность без рестарта процесса.</summary>
    public async Task RecreateAsync() =>
        await ExecuteAdminAsync($"CREATE DATABASE \"{_databaseName}\"");

    private async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        // Пул соединений конкретной БД иначе удержит её и DROP не выполнится.
        NpgsqlConnection.ClearPool(new NpgsqlConnection(ConnectionString));

        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
    }
}
