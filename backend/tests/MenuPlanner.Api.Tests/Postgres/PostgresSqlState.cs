namespace MenuPlanner.Api.Tests.Postgres;

/// <summary>Коды ошибок PostgreSQL, на которые опираются проверки гарантий.</summary>
internal static class PostgresSqlState
{
    public const string UniqueViolation = "23505";
    public const string ForeignKeyViolation = "23503";
    public const string NumericValueOutOfRange = "22003";
}
