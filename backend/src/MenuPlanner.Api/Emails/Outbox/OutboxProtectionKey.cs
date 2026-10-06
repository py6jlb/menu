using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace MenuPlanner.Api.Emails.Outbox;

/// <summary>
/// Выводит ключ защиты содержимого очереди. По умолчанию используется
/// <c>EMAIL_OUTBOX_KEY</c>, иначе ключ выводится из секрета JWT через HKDF с
/// отдельной меткой — это разные назначения ключа, и ключ остаётся стабильным
/// между перезапусками, пока стабильна конфигурация.
/// </summary>
public static class OutboxProtectionKey
{
    public const string Variable = "EMAIL_OUTBOX_KEY";

    private const int KeyLength = 32;
    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("menu-planner-outbox-salt-v1");
    private static readonly byte[] Info = Encoding.UTF8.GetBytes("menu-planner:email-outbox:v1");

    public static byte[] Derive(IConfiguration configuration, string jwtSecret)
    {
        var configured = configuration[Variable];
        var secret = !string.IsNullOrWhiteSpace(configured) ? configured : jwtSecret;
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException(
                "Конфигурация: не задан ключ защиты очереди писем (EMAIL_OUTBOX_KEY или JWT_SECRET)");

        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(secret),
            KeyLength,
            Salt,
            Info);
    }
}
