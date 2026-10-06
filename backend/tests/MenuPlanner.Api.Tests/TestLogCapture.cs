using Microsoft.Extensions.Logging;

namespace MenuPlanner.Api.Tests;

/// <summary>Перехват структурированных записей журнала для проверок.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly List<CapturedLog> _logs = new();

    public IReadOnlyList<CapturedLog> Logs
    {
        get
        {
            lock (_gate) return _logs.ToArray();
        }
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Add(CapturedLog log)
    {
        lock (_gate) _logs.Add(log);
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly CapturingLoggerProvider _provider;
        private readonly string _category;

        public CapturingLogger(CapturingLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
                foreach (var pair in values)
                    properties[pair.Key] = pair.Value;
            _provider.Add(new CapturedLog(_category, logLevel, formatter(state, exception), properties));
        }
    }
}

internal sealed record CapturedLog(
    string Category,
    LogLevel Level,
    string Message,
    IReadOnlyDictionary<string, object?> Properties);
