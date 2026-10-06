using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using MenuPlanner.Api.Auth.Codes;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Emails;
using MenuPlanner.Api.Emails.Outbox;
using MenuPlanner.Api.Tests;

namespace MenuPlanner.Api.Tests.Postgres;

/// <summary>
/// Гарантии надёжной доставки на настоящей PostgreSQL: атомарность постановки,
/// конкурентный захват без двойной отправки и повтор к тому же challenge.
/// </summary>
public sealed class PostgresEmailOutboxTests : PostgresTestBase
{
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly IPasswordHasher<User> Hasher = new PasswordHasher<User>();
    private static readonly EmailOptions SenderOptions = new()
    {
        From = "noreply@menu.local",
        FromName = "Меню для домохозяек"
    };

    [PostgresFact]
    public async Task ChallengeAndAcceptedDelivery_AreCommittedAndRolledBackTogether()
    {
        var clock = new FakeTimeProvider(FixedNow);

        await using (var rolledBack = NewContext())
        {
            await using var tx = await rolledBack.Database.BeginTransactionAsync();
            var user = await SeedUserAsync(rolledBack, verified: false);
            var challenge = AuthCodePolicy.Create(
                Hasher, user.Id, AuthCodeType.Verify, "111111", FixedNow.UtcDateTime);
            rolledBack.AuthCodes.Add(challenge);
            await TestOutbox.NewOutbox(rolledBack, clock).EnqueueAsync(user, challenge, "111111");
            await rolledBack.SaveChangesAsync();
            await tx.RollbackAsync();
        }

        await using (var verify = NewContext())
        {
            Assert.False(await verify.AuthCodes.AnyAsync());
            Assert.False(await verify.EmailOutboxMessages.AnyAsync());
        }

        await using (var committed = NewContext())
        {
            await using var tx = await committed.Database.BeginTransactionAsync();
            var user = await SeedUserAsync(committed, verified: false);
            var challenge = AuthCodePolicy.Create(
                Hasher, user.Id, AuthCodeType.Verify, "222222", FixedNow.UtcDateTime);
            committed.AuthCodes.Add(challenge);
            await TestOutbox.NewOutbox(committed, clock).EnqueueAsync(user, challenge, "222222");
            await committed.SaveChangesAsync();
            await tx.CommitAsync();
        }

        await using (var verify = NewContext())
        {
            Assert.True(await verify.AuthCodes.AnyAsync());
            Assert.True(await verify.EmailOutboxMessages.AnyAsync());
        }
    }

    [PostgresFact]
    public async Task Registration_OnPostgres_CommitsAccountChallengeAndOutbox_ThenDelivers()
    {
        using var factory = new PostgresApiFactory(Database.ConnectionString);
        using var client = factory.CreateClient();
        var email = $"register-outbox-{Guid.NewGuid():N}@example.com";

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", new { email, password = "secret1" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.Users.AnyAsync(u => u.Email == email));
        Assert.Single(await db.AuthCodes.ToListAsync());
        // Доставка по умолчанию (logging transport) успешна: запись удалена.
        Assert.Empty(await db.EmailOutboxMessages.ToListAsync());
    }

    [PostgresFact]
    public async Task ConcurrentWorkers_DeliverEachAcceptedMessageOnce()
    {
        var clock = new FakeTimeProvider(FixedNow);
        await using (var seed = NewContext())
        {
            await SeedQueuedAsync(seed, clock, "123456");
            await seed.SaveChangesAsync();
        }

        var transport = new SwitchableEmailTransport();
        var barrier = new AsyncBarrier(2);
        var options = new EmailOutboxOptions();

        await Task.WhenAll(
            DispatchAsync(NewContext(new BarrierNonQueryInterceptor(barrier)), transport, clock, options),
            DispatchAsync(NewContext(new BarrierNonQueryInterceptor(barrier)), transport, clock, options));

        var letter = Assert.Single(transport.Sent);
        Assert.Contains("123456", letter.HtmlBody);

        await using var verify = NewContext();
        Assert.False(await verify.EmailOutboxMessages.AnyAsync());
    }

    [PostgresFact]
    public async Task TransientFailure_RetriesSameChallenge_WithoutNewCode()
    {
        var clock = new FakeTimeProvider(FixedNow);
        Guid userId;
        await using (var seed = NewContext())
        {
            var user = await SeedQueuedAsync(seed, clock, "654321");
            userId = user.Id;
            await seed.SaveChangesAsync();
        }

        var transport = new SwitchableEmailTransport
        {
            FailureFactory = _ => SwitchableEmailTransport.Transient()
        };
        var options = new EmailOutboxOptions { RetryBaseDelaySeconds = 1 };

        await using (var first = NewContext())
        {
            var result = await DispatchAsync(first, transport, clock, options);
            Assert.Equal(1, result.Rescheduled);
        }

        clock.Advance(TimeSpan.FromSeconds(2));
        transport.FailureFactory = null;

        await using (var second = NewContext())
        {
            var result = await DispatchAsync(second, transport, clock, options);
            Assert.Equal(1, result.Delivered);
        }

        Assert.Contains("654321", Assert.Single(transport.Sent).HtmlBody);

        await using var verify = NewContext();
        Assert.Equal(1, await verify.AuthCodes.CountAsync(c => c.UserId == userId));
    }

    [PostgresFact]
    public async Task ConsumedChallenge_IsNotDeliveredAsActiveCode()
    {
        var clock = new FakeTimeProvider(FixedNow);
        await using (var seed = NewContext())
        {
            var user = await SeedQueuedAsync(seed, clock, "999999");
            var challenge = await seed.AuthCodes.SingleAsync(c => c.UserId == user.Id);
            challenge.Used = true;
            await seed.SaveChangesAsync();
        }

        var transport = new SwitchableEmailTransport();
        await using (var db = NewContext())
        {
            var result = await DispatchAsync(db, transport, clock, new EmailOutboxOptions());
            Assert.Equal(1, result.Abandoned);
        }

        Assert.Empty(transport.Sent);
        await using var verify = NewContext();
        Assert.False(await verify.EmailOutboxMessages.AnyAsync());
    }

    private AppDbContext NewContext(BarrierNonQueryInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Database.ConnectionString);
        if (interceptor is not null)
            builder.AddInterceptors(interceptor);
        return new AppDbContext(builder.Options);
    }

    private static Task<OutboxDispatchResult> DispatchAsync(
        AppDbContext db, IEmailTransport transport, TimeProvider clock, EmailOutboxOptions options)
    {
        var processor = new EmailOutboxProcessor(
            db,
            new EmailSender(SenderOptions, transport),
            TestOutbox.Protector(),
            options,
            clock,
            NullLogger<EmailOutboxProcessor>.Instance);
        return processor.DispatchDueAsync();
    }

    private static async Task<User> SeedUserAsync(AppDbContext db, bool verified)
    {
        var user = PostgresData.NewUser($"outbox-{Guid.NewGuid():N}@example.com");
        user.IsEmailVerified = verified;
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<User> SeedQueuedAsync(
        AppDbContext db, TimeProvider clock, string code,
        AuthCodeType type = AuthCodeType.Verify, bool verified = false)
    {
        var user = await SeedUserAsync(db, verified);
        var challenge = AuthCodePolicy.Create(Hasher, user.Id, type, code, FixedNow.UtcDateTime);
        db.AuthCodes.Add(challenge);
        await TestOutbox.NewOutbox(db, clock).EnqueueAsync(user, challenge, code);
        await db.SaveChangesAsync();
        return user;
    }
}
