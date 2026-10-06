namespace MenuPlanner.Api.Domain;

/// <summary>
/// Ограничения семьи, согласованные с хранилищем. Статические правила без
/// состояния: используются валидацией и юнит-тестируются напрямую.
/// </summary>
public static class FamilyCatalog
{
    /// <summary>Согласовано с хранилищем: Family.Name — varchar(200).</summary>
    public const int NameMaxLength = 200;

    /// <summary>Согласовано с хранилищем: Family.InviteCode — varchar(32).</summary>
    public const int InviteCodeMaxLength = 32;
}
