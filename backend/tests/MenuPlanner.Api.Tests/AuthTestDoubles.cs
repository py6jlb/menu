using MenuPlanner.Api.Auth.Codes;

namespace MenuPlanner.Api.Tests;

/// <summary>Управляемое время тестов: без ожиданий и без зависимости от часов хоста.</summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public FakeTimeProvider(DateTimeOffset start) => _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;

    public void Set(DateTimeOffset value) => _now = value;
}

/// <summary>Детерминированный источник кодов: тест задаёт точную последовательность.</summary>
internal sealed class QueueCodeGenerator : IAuthCodeGenerator
{
    private readonly Queue<string> _codes;

    public QueueCodeGenerator(params string[] codes) => _codes = new Queue<string>(codes);

    public QueueCodeGenerator(IEnumerable<string> codes) => _codes = new Queue<string>(codes);

    public string Generate() => _codes.Count > 0 ? _codes.Dequeue() : "000000";
}
