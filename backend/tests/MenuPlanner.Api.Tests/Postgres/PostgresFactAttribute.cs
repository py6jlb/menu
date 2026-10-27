using System.Runtime.CompilerServices;
using Xunit;

namespace MenuPlanner.Api.Tests.Postgres;

/// <summary>
/// <see cref="FactAttribute"/>, который пропускается, когда PostgreSQL-сервер
/// не сконфигурирован. Skip вычисляется при discovery, поэтому быстрый suite
/// остаётся зелёным без Docker и настоящей БД.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute([CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
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
    public PostgresTheoryAttribute([CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!PostgresTestEnvironment.IsConfigured)
            Skip = PostgresFactAttribute.SkipReason;
    }
}
