using System.Text.RegularExpressions;

namespace MenuPlanner.Api.Auth;

/// <summary>
/// Единое правило нормализации и проверки email для публичной регистрации и
/// закрытого административного bootstrap: обе процедуры принимают адрес
/// одинаково, но по-разному распоряжаются ролью.
/// </summary>
public static class EmailPolicy
{
    private static readonly Regex Pattern = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Normalize(string? email) =>
        email?.Trim().ToLowerInvariant() ?? "";

    public static bool IsValid(string email) => Pattern.IsMatch(email);
}
