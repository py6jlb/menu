using System.Security.Cryptography;
using System.Text;

namespace MenuPlanner.Api.Emails.Outbox;

/// <summary>
/// Защита содержимого очереди через AES-GCM: случайный nonce на запись,
/// аутентифицированный шифртекст. Формат значения — base64(nonce|tag|cipher).
/// </summary>
public sealed class AesGcmOutboxPayloadProtector : IOutboxPayloadProtector
{
    private const int NonceLength = 12;
    private const int TagLength = 16;

    private readonly byte[] _key;

    public AesGcmOutboxPayloadProtector(byte[] key)
    {
        if (key.Length != 32)
            throw new ArgumentException("Ключ защиты очереди должен быть 32 байта.", nameof(key));
        _key = key;
    }

    public string Protect(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagLength];

        using (var aes = new AesGcm(_key, TagLength))
            aes.Encrypt(nonce, plain, cipher, tag);

        var payload = new byte[NonceLength + TagLength + cipher.Length];
        nonce.CopyTo(payload, 0);
        tag.CopyTo(payload, NonceLength);
        cipher.CopyTo(payload, NonceLength + TagLength);
        return Convert.ToBase64String(payload);
    }

    public bool TryUnprotect(string protectedPayload, out string plaintext)
    {
        plaintext = "";
        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(protectedPayload);
        }
        catch (FormatException)
        {
            return false;
        }

        if (payload.Length < NonceLength + TagLength)
            return false;

        var nonce = payload.AsSpan(0, NonceLength);
        var tag = payload.AsSpan(NonceLength, TagLength);
        var cipher = payload.AsSpan(NonceLength + TagLength);
        var plain = new byte[cipher.Length];

        try
        {
            using var aes = new AesGcm(_key, TagLength);
            aes.Decrypt(nonce, cipher, tag, plain);
        }
        catch (CryptographicException)
        {
            return false;
        }

        plaintext = Encoding.UTF8.GetString(plain);
        return true;
    }
}
