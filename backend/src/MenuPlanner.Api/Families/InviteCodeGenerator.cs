using System.Security.Cryptography;
using System.Text;

namespace MenuPlanner.Api.Families;

/// <summary>Генерация инвайт-кода семьи: 8 символов без неоднозначных букв и цифр.</summary>
public static class InviteCodeGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 8;

    public static string Generate()
    {
        var buffer = new byte[CodeLength];
        var sb = new StringBuilder(CodeLength);

        RandomNumberGenerator.Fill(buffer);
        foreach (var b in buffer)
            sb.Append(Alphabet[b % Alphabet.Length]);

        return sb.ToString();
    }
}
