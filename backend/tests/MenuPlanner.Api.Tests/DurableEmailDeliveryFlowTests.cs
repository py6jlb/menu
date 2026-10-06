using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Emails;
using MenuPlanner.Api.Emails.Outbox;

namespace MenuPlanner.Api.Tests;

/// <summary>
/// Сквозной путь регистрации/повторной отправки: аккаунт и принятая доставка
/// фиксируются вместе, сессия выдаётся сразу, а письмо доводится даже после
/// временного отказа транспорта.
/// </summary>
public sealed class DurableEmailDeliveryFlowTests
{
    [Fact]
    public async Task Register_PersistsAccountChallengeAndAcceptedDelivery_ThenDelivers()
    {
        var transport = new SwitchableEmailTransport();
        using var factory = NewFactory(transport);
        using var client = factory.CreateClient();
        var email = $"durable-{Guid.NewGuid():N}@example.com";

        var response = await RegisterAsync(client, email);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(auth);
        Assert.False(string.IsNullOrWhiteSpace(auth.Token));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.EmailOutboxMessages.ToListAsync());
        var challenge = await db.AuthCodes.SingleAsync();
        Assert.False(challenge.Used);

        var letter = Assert.Single(transport.Sent);
        Assert.Equal(email, letter.To);
        Assert.Contains("Код подтверждения почты", letter.Subject);
    }

    [Fact]
    public async Task Register_WhenTransportFails_KeepsSessionAndDeliversAfterRecovery()
    {
        var transport = new SwitchableEmailTransport
        {
            FailureFactory = _ => SwitchableEmailTransport.Transient()
        };
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var factory = NewFactory(transport);
        factory.ConfigureTestServices = services =>
        {
            services.AddSingleton<IEmailTransport>(transport);
            services.AddSingleton<TimeProvider>(clock);
        };
        using var client = factory.CreateClient();
        var email = $"recover-{Guid.NewGuid():N}@example.com";

        var response = await RegisterAsync(client, email);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Empty(transport.Sent);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var queued = await db.EmailOutboxMessages.SingleAsync();
            Assert.Equal(EmailOutboxStatus.Pending, queued.Status);
            Assert.DoesNotContain("123456", queued.ProtectedPayload);
        }

        // Транспорт восстановился после задержки повтора: та же сохранённая
        // очередь доводит письмо, новый код не создаётся.
        transport.FailureFactory = null;
        clock.Advance(TimeSpan.FromMinutes(5));
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<EmailDispatchTrigger>().TryDispatchAsync();
        }

        Assert.Single(transport.Sent);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Empty(await db.EmailOutboxMessages.ToListAsync());
            Assert.Single(await db.AuthCodes.ToListAsync());
        }
    }

    [Fact]
    public async Task Register_WhenAcceptanceCannotBePersisted_RollsBackTheAccount()
    {
        var transport = new SwitchableEmailTransport();
        using var factory = NewFactory(transport);
        factory.ConfigureTestServices = services =>
            services.AddSingleton<IOutboxPayloadProtector>(new ThrowingOutboxProtector());
        using var client = factory.CreateClient();
        var email = $"atomic-{Guid.NewGuid():N}@example.com";

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", new { email, password = "secret1" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Users.AnyAsync(u => u.Email == email));
        Assert.Empty(await db.AuthCodes.ToListAsync());
        Assert.Empty(await db.EmailOutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task Resend_WhenTransportFails_AcceptsAndQueuesWithoutFalseError()
    {
        var transport = new SwitchableEmailTransport();
        using var factory = NewFactory(transport);
        factory.Settings["AUTH_CODE_RESEND_COOLDOWN_MINUTES"] = "0";
        using var client = factory.CreateClient();
        var email = $"resend-durable-{Guid.NewGuid():N}@example.com";
        var auth = await RegisterAsync(client, email);
        var token = (await auth.Content.ReadFromJsonAsync<AuthResponse>())!.Token;

        transport.FailureFactory = _ => SwitchableEmailTransport.Transient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/verify/resend");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { });
        using var resend = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(await db.EmailOutboxMessages.ToListAsync());
    }

    private static ApiFactory NewFactory(SwitchableEmailTransport transport) =>
        new()
        {
            AutoVerifyEmailsOnRegistration = false,
            ConfigureTestServices = services => services.AddSingleton<IEmailTransport>(transport)
        };

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/auth/register", new { email, password = "secret1" });
}
