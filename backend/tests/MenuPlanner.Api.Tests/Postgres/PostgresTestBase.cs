using MenuPlanner.Api.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuPlanner.Api.Tests.Postgres;

/// <summary>
/// База теста создаётся перед каждым тестом и удаляется после него, в том числе
/// при падении: изоляция данных и очистка временных ресурсов гарантированы
/// жизненным циклом xUnit. По умолчанию схема поднимается штатными миграциями;
/// проверки самого пути миграций отключают это через <see cref="MigrateOnInitialize"/>.
/// </summary>
public abstract class PostgresTestBase : IAsyncLifetime
{
    protected PostgresDatabase Database { get; private set; } = null!;

    protected virtual bool MigrateOnInitialize => true;

    public async Task InitializeAsync()
    {
        Database = await PostgresDatabase.CreateAsync();

        if (MigrateOnInitialize)
        {
            await using var db = Database.CreateContext();
            await db.Database.MigrateAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (Database is null)
            return;

        await Database.DisposeAsync();
    }
}
