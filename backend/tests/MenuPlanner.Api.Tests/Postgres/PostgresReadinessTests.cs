using System.Net;
using Xunit;

namespace MenuPlanner.Api.Tests.Postgres;

/// <summary>
/// Настоящий <see cref="MenuPlanner.Api.Health.DatabaseReadinessProbe"/> поверх
/// PostgreSQL: недоступная БД даёт 503, возвращение БД — снова 200 без
/// перезапуска процесса. Недоступность эмулируется снятием БД (DROP ... FORCE),
/// поэтому проверяется реальный путь Npgsql и восстановление пула соединений.
/// </summary>
public sealed class PostgresReadinessTests : PostgresTestBase
{
    [PostgresFact]
    public async Task Ready_TracksDatabaseAvailability_WithoutRestart()
    {
        using var factory = new PostgresApiFactory(Database.ConnectionString);
        using var client = factory.CreateClient();

        var ready = await client.GetAsync("/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Contains("\"status\":\"ready\"", await ready.Content.ReadAsStringAsync());

        await Database.DropAsync();
        var unavailable = await client.GetAsync("/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Contains("\"status\":\"not-ready\"", await unavailable.Content.ReadAsStringAsync());

        await Database.RecreateAsync();
        var recovered = await client.GetAsync("/ready");
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Contains("\"status\":\"ready\"", await recovered.Content.ReadAsStringAsync());
    }
}
