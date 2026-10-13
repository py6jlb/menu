using System.Security.Cryptography;
using System.Text;
using MenuPlanner.Infrastructure.Data;
using MenuPlanner.Infrastructure.Emails.Outbox;

namespace MenuPlanner.Api.Tests.Support;

/// <summary>Стабильный ключ и фабрики очереди писем для тестов.</summary>
internal static class TestOutbox
{
    public static readonly byte[] Key =
        SHA256.HashData(Encoding.UTF8.GetBytes("menu-planner-test-outbox-key"));

    public static IOutboxPayloadProtector Protector() => new AesGcmOutboxPayloadProtector(Key);

    public static EmailOutbox NewOutbox(AppDbContext db, TimeProvider clock) =>
        new(db, Protector(), clock);
}
