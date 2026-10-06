using System.Text.RegularExpressions;

namespace MenuPlanner.Api.Auth;

/// <summary>
/// Единое правило нормализации и проверки email для публичной регистрации и
/// закрытого административного bootstrap: обе процедуры принимают адрес
/// одинаково, но по-разному распоряжаются ролью.
/// </summary>
public static class EmailPolicy
{
    /// <summary>Согласовано с хранилищем: User.Email — varchar(320).</summary>
    public const int MaxLength = 320;

    private static readonly Regex Pattern = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Normalize(string? email) =>
        email?.Trim().ToLowerInvariant() ?? "";

    public static bool IsValid(string email) =>
        email.Length <= MaxLength && Pattern.IsMatch(email);
}
