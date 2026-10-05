using MenuPlanner.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MenuPlanner.Api.Tests.Postgres;

public sealed class PostgresInterleavingTests : PostgresTestBase
{
    [PostgresFact]
    public async Task TwoIndependentContexts_InterleaveDeterministically_OnUniqueConflict()
    {
        var owner = PostgresData.NewUser("owner@example.com");
        await using (var seed = Database.CreateContext())
        {
            seed.Users.Add(owner);
            await seed.SaveChangesAsync();
        }

        const string inviteCode = "RACE-1";
        var barrier = new AsyncBarrier(2);

        async Task<string> InsertFamilyAsync(string name)
        {
            await using var db = Database.CreateContext();
            db.Families.Add(PostgresData.NewFamily(name, inviteCode, owner.Id));

            // Обе операции доходят до вставки раньше, чем любая зафиксирует её;
            // момент пересечения задан кодом, а не задержкой.
            await barrier.SignalAndWaitAsync();

            try
            {
                await db.SaveChangesAsync();
                return "success";
            }
            catch (DbUpdateException exception)
                when (exception.InnerException is PostgresException
                    { SqlState: PostgresSqlState.UniqueViolation })
            {
                return "conflict";
            }
        }

        var results = await Task.WhenAll(InsertFamilyAsync("Первая"), InsertFamilyAsync("Вторая"));

        Assert.Equal(1, results.Count(result => result == "success"));
        Assert.Equal(1, results.Count(result => result == "conflict"));

        await using var verify = Database.CreateContext();
        Assert.Equal(1, await verify.Families.CountAsync(f => f.InviteCode == inviteCode));
    }
}
