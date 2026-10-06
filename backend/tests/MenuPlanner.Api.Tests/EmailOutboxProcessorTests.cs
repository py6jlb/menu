using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using MenuPlanner.Api.Auth.Codes;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Emails;
using MenuPlanner.Api.Emails.Outbox;

namespace MenuPlanner.Api.Tests;

/// <summary>
/// Поведение очереди на управляемом времени и подменяемом транспорте: принятая
/// доставка доводится, повторяется после временного отказа, сдаётся после
/// окончательного, а неактуальный challenge не отправляется.
/// </summary>
public sealed class EmailOutboxProcessorTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly IPasswordHasher<User> Hasher = new PasswordHasher<User>();
    private static readonly EmailOptions SenderOptions = new()
    {
        From = "noreply@menu.local",
        FromName = "Меню для домохозяек"
    };

    [Fact]
    public async Task Delivered_RemovesQueuedRecord_AndSendsCodeOnce()
    {
        var clock = new FakeTimeProvider(Start);
        await using var db = NewContext();
        await EnqueueAsync(db, clock, AuthCodeType.Verify, "111111", Start.UtcDateTime);
        var transport = new SwitchableEmailTransport();
        var processor = NewProcessor(db, transport, clock, new EmailOutboxOptions());

        var result = await processor.DispatchDueAsync();

        Assert.Equal(1, result.Delivered);
        var message = Assert.Single(transport.Sent);
        Assert.Contains("111111", message.HtmlBody);
        Assert.Empty(await db.EmailOutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task ProtectedPayload_DoesNotContainPlaintextCode_ButSurvivesRestart()
    {
        var clock = new FakeTimeProvider(Start);
        var store = NewContextOptions();

        await using (var first = new AppDbContext(store))
        {
            await EnqueueAsync(first, clock, AuthCodeType.Verify, "654321", Start.UtcDateTime);
            var stored = await first.EmailOutboxMessages.AsNoTracking().SingleAsync();
            Assert.DoesNotContain("654321", stored.ProtectedPayload);
        }

        // «Перезапуск»: новый контекст и новый экземпляр процессора с тем же ключом.
        var transport = new SwitchableEmailTransport();
        await using (var second = new AppDbContext(store))
        {
            var processor = NewProcessor(second, transport, clock, new EmailOutboxOptions());
            Assert.Equal(1, (await processor.DispatchDueAsync()).Delivered);
        }

        Assert.Contains("654321", Assert.Single(transport.Sent).HtmlBody);
    }

    [Fact]
    public async Task TransientFailure_Reschedules_ThenRetryDeliversSameCode()
    {
        var clock = new FakeTimeProvider(Start);
        await using var db = NewContext();
        await EnqueueAsync(db, clock, AuthCodeType.Verify, "222222", Start.UtcDateTime);
        var transport = new SwitchableEmailTransport
        {
            FailureFactory = _ => SwitchableEmailTransport.Transient()
        };
        var options = new EmailOutboxOptions { RetryBaseDelaySeconds = 1 };
        var processor = NewProcessor(db, transport, clock, options);

        var first = await processor.DispatchDueAsync();
        Assert.Equal(1, first.Rescheduled);
        var pending = await db.EmailOutboxMessages.SingleAsync();
        Assert.Equal(EmailOutboxStatus.Pending, pending.Status);
        Assert.Equal(1, pending.Attempts);
        Assert.True(pending.NextAttemptAt > Start.UtcDateTime);
        Assert.Empty(transport.Sent);

        transport.FailureFactory = null;
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(1, (await processor.DispatchDueAsync()).Delivered);

        // Тот же challenge, а не новый код.
        Assert.Contains("222222", Assert.Single(transport.Sent).HtmlBody);
        Assert.Equal(1, await db.AuthCodes.CountAsync());
    }

    [Fact]
    public async Task PermanentFailure_StopsAfterBoundedAttempts()
    {
        var clock = new FakeTimeProvider(Start);
        await using var db = NewContext();
        await EnqueueAsync(db, clock, AuthCodeType.Verify, "333333", Start.UtcDateTime);
        var transport = new SwitchableEmailTransport
        {
            FailureFactory = _ => SwitchableEmailTransport.Transient()
        };
        var options = new EmailOutboxOptions { RetryBaseDelaySeconds = 1, MaxAttempts = 3 };
        var processor = NewProcessor(db, transport, clock, options);

        for (var i = 0; i < 3; i++)
        {
            await processor.DispatchDueAsync();
            clock.Advance(TimeSpan.FromHours(1));
        }

        var stored = await db.EmailOutboxMessages.SingleAsync();
        Assert.Equal(EmailOutboxStatus.Failed, stored.Status);
        Assert.Equal(3, stored.Attempts);

        // Окончательно проваленное не отправляется даже при рабочем транспорте.
        transport.FailureFactory = null;
        Assert.Equal(0, (await processor.DispatchDueAsync()).Claimed);
        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task ExpiredChallenge_IsNotSent_AndIsRemoved()
    {
        var clock = new FakeTimeProvider(Start);
        await using var db = NewContext();
        await EnqueueAsync(db, clock, AuthCodeType.Verify, "444444", Start.UtcDateTime);
        var transport = new SwitchableEmailTransport();
        var processor = NewProcessor(db, transport, clock, new EmailOutboxOptions());

        clock.Advance(AuthCodePolicy.VerifyLifetime + TimeSpan.FromMinutes(1));
        var result = await processor.DispatchDueAsync();

        Assert.Equal(1, result.Abandoned);
        Assert.Empty(transport.Sent);
        Assert.Empty(await db.EmailOutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task ReplacedOrConsumedChallenge_IsNotSent()
    {
        var clock = new FakeTimeProvider(Start);
        await using var db = NewContext();
        await EnqueueAsync(db, clock, AuthCodeType.Verify, "555555", Start.UtcDateTime, used: true);
        var transport = new SwitchableEmailTransport();
        var processor = NewProcessor(db, transport, clock, new EmailOutboxOptions());

        var result = await processor.DispatchDueAsync();

        Assert.Equal(1, result.Abandoned);
        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task VerifiedUser_DoesNotGetVerificationCode()
    {
        var clock = new FakeTimeProvider(Start);
        await using var db = NewContext();
        await EnqueueAsync(db, clock, AuthCodeType.Verify, "666666", Start.UtcDateTime, verified: true);
        var transport = new SwitchableEmailTransport();
        var processor = NewProcessor(db, transport, clock, new EmailOutboxOptions());

        await processor.DispatchDueAsync();

        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task StaleInProgressClaim_IsReclaimed()
    {
        var clock = new FakeTimeProvider(Start);
        await using var db = NewContext();
        await EnqueueAsync(db, clock, AuthCodeType.Verify, "777777", Start.UtcDateTime);
        var queued = await db.EmailOutboxMessages.SingleAsync();
        queued.Status = EmailOutboxStatus.InProgress;
        queued.ClaimToken = Guid.NewGuid();
        queued.ClaimedAt = Start.UtcDateTime - TimeSpan.FromMinutes(10);
        await db.SaveChangesAsync();

        var transport = new SwitchableEmailTransport();
        var options = new EmailOutboxOptions { LeaseSeconds = 60 };
        var processor = NewProcessor(db, transport, clock, options);

        Assert.Equal(1, (await processor.DispatchDueAsync()).Delivered);
        Assert.Contains("777777", Assert.Single(transport.Sent).HtmlBody);
    }

    [Fact]
    public async Task FailedRecords_ArePurgedAfterRetention()
    {
        var clock = new FakeTimeProvider(Start);
        await using var db = NewContext();
        await EnqueueAsync(db, clock, AuthCodeType.Verify, "888888", Start.UtcDateTime);
        var queued = await db.EmailOutboxMessages.SingleAsync();
        queued.Status = EmailOutboxStatus.Failed;
        queued.Attempts = 3;
        await db.SaveChangesAsync();

        var transport = new SwitchableEmailTransport();
        var options = new EmailOutboxOptions { RetentionDays = 7 };
        var processor = NewProcessor(db, transport, clock, options);

        clock.Advance(TimeSpan.FromDays(8));
        await processor.DispatchDueAsync();

        Assert.Empty(await db.EmailOutboxMessages.ToListAsync());
        Assert.Empty(transport.Sent);
    }

    private static DbContextOptions<AppDbContext> NewContextOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("outbox-" + Guid.NewGuid().ToString("N"))
            .Options;

    private static AppDbContext NewContext() => new(NewContextOptions());

    private static EmailOutboxProcessor NewProcessor(
        AppDbContext db, IEmailTransport transport, TimeProvider clock, EmailOutboxOptions options) =>
        new(db,
            new EmailSender(SenderOptions, transport),
            TestOutbox.Protector(),
            options,
            clock,
            NullLogger<EmailOutboxProcessor>.Instance);

    private static async Task EnqueueAsync(
        AppDbContext db,
        TimeProvider clock,
        AuthCodeType type,
        string code,
        DateTime createdAt,
        bool verified = false,
        bool used = false)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@example.com",
            PasswordHash = "hash",
            CreatedAt = createdAt,
            IsEmailVerified = verified
        };
        var challenge = AuthCodePolicy.Create(Hasher, user.Id, type, code, createdAt);
        challenge.Used = used;
        db.Users.Add(user);
        db.AuthCodes.Add(challenge);
        await db.SaveChangesAsync();

        await TestOutbox.NewOutbox(db, clock).EnqueueAsync(user, challenge, code);
        await db.SaveChangesAsync();
    }
}
