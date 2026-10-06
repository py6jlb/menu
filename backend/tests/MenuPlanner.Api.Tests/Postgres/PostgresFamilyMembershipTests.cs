using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Families;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuPlanner.Api.Tests.Postgres;

/// <summary>
/// Гонка уникального членства под настоящей PostgreSQL: два параллельных
/// создания/вступления одного пользователя сходятся детерминированно, проигравший
/// получает конфликт вместо 500, а в БД остаётся ровно одна семья и одно членство.
/// Точка пересечения задаётся барьером на команде вставки, без sleep.
/// </summary>
public sealed class PostgresFamilyMembershipTests : PostgresTestBase
{
    [PostgresFact]
    public async Task ConcurrentCreate_KeepsSingleFamilyAndMembership_LoserConflicts()
    {
        var user = PostgresData.NewUser($"create-race-{Guid.NewGuid():N}@example.com");
        await using (var seed = Database.CreateContext())
        {
            seed.Users.Add(user);
            await seed.SaveChangesAsync();
        }

        var principal = PrincipalFor(user.Id);
        var barrier = new AsyncBarrier(2);

        async Task<FamilyAccess> CreateAsync()
        {
            await using var db = NewContext(
                new BarrierNonQueryInterceptor(barrier, "INSERT INTO \"FamilyMembers\""));
            return await new FamilyService(db).CreateAsync("Семья", principal);
        }

        var results = await Task.WhenAll(CreateAsync(), CreateAsync());

        Assert.Single(results, r => r.Outcome == FamilyOutcome.Ok);
        var conflict = Assert.Single(results, r => r.Outcome == FamilyOutcome.Conflict);
        Assert.Equal("Вы уже состоите в семье.", conflict.Error);

        await using var verify = Database.CreateContext();
        Assert.Equal(1, await verify.Families.CountAsync());
        Assert.Equal(1, await verify.FamilyMembers.CountAsync(m => m.UserId == user.Id));
    }

    [PostgresFact]
    public async Task ConcurrentJoin_KeepsSingleMembership_LoserConflicts()
    {
        var owner = PostgresData.NewUser($"join-owner-{Guid.NewGuid():N}@example.com");
        var member = PostgresData.NewUser($"join-race-{Guid.NewGuid():N}@example.com");
        const string inviteCode = "JOIN-RACE";
        var family = PostgresData.NewFamily("Семья", inviteCode, owner.Id);

        await using (var seed = Database.CreateContext())
        {
            seed.Users.AddRange(owner, member);
            seed.Families.Add(family);
            seed.FamilyMembers.Add(new FamilyMember
            {
                FamilyId = family.Id,
                UserId = owner.Id,
                JoinedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        var principal = PrincipalFor(member.Id);
        var barrier = new AsyncBarrier(2);

        async Task<FamilyAccess> JoinAsync()
        {
            await using var db = NewContext(
                new BarrierNonQueryInterceptor(barrier, "INSERT INTO \"FamilyMembers\""));
            return await new FamilyService(db).JoinAsync(inviteCode, principal);
        }

        var results = await Task.WhenAll(JoinAsync(), JoinAsync());

        Assert.Single(results, r => r.Outcome == FamilyOutcome.Ok);
        var conflict = Assert.Single(results, r => r.Outcome == FamilyOutcome.Conflict);
        Assert.Equal("Вы уже состоите в семье.", conflict.Error);

        await using var verify = Database.CreateContext();
        Assert.Equal(1, await verify.FamilyMembers.CountAsync(m => m.UserId == member.Id));
        Assert.Equal(2, await verify.FamilyMembers.CountAsync(m => m.FamilyId == family.Id));
    }

    private AppDbContext NewContext(BarrierNonQueryInterceptor interceptor)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Database.ConnectionString);
        builder.AddInterceptors(interceptor);
        return new AppDbContext(builder.Options);
    }

    private static ClaimsPrincipal PrincipalFor(Guid userId) =>
        new(new ClaimsIdentity(
            new[] { new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()) }));
}
