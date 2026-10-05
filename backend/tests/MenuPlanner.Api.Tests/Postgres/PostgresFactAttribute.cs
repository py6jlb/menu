using Xunit;

namespace MenuPlanner.Api.Tests.Postgres;

/// <summary>
/// <see cref="FactAttribute"/>, который пропускается, когда PostgreSQL-сервер
/// не сконфигурирован. Skip вычисляется при discovery, поэтому быстрый suite
/// остаётся зелёным без Docker и настоящей БД.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (!PostgresTestEnvironment.IsConfigured)
            Skip = SkipReason;
    }

    internal const string SkipReason =
        "PostgreSQL не сконфигурирован: задайте " + PostgresTestEnvironment.VariableName +
        " (см. scripts/test-postgres.sh).";
}

/// <summary>Теоретический аналог <see cref="PostgresFactAttribute"/>.</summary>
public sealed class PostgresTheoryAttribute : TheoryAttribute
{
    public PostgresTheoryAttribute()
    {
        if (!PostgresTestEnvironment.IsConfigured)
            Skip = PostgresFactAttribute.SkipReason;
    }
}
