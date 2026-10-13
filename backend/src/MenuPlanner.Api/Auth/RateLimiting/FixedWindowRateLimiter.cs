namespace MenuPlanner.Api.Auth;

/// <summary>
/// Буфер фиксированных окон в памяти процесса. Потокобезопасен, число хранимых
/// ключей ограничено <see cref="_maxKeys"/>, устаревшие окна удаляются. Когда
/// хранилище заполнено неистёкшими окнами, новые ключи отклоняются до
/// освобождения места (fail-closed для новых ключей, уже отслеживаемые
/// продолжают работать). Ограничение действует на одну реплику: при
/// масштабировании нужен общий store (см. ADR-0010).
/// </summary>
public sealed class FixedWindowRateLimiter
{
    public const int DefaultMaxKeys = 10_000;
    private const int PruneEveryOperations = 256;
    private static readonly TimeSpan PruneInterval = TimeSpan.FromMinutes(1);

    private readonly object _gate = new();
    private readonly Dictionary<string, Window> _windows = new();
    private readonly int _maxKeys;
    private long _operations;
    private DateTime? _lastPruneAt;

    public FixedWindowRateLimiter() : this(DefaultMaxKeys) { }

    public FixedWindowRateLimiter(int maxKeys)
    {
        if (maxKeys <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxKeys), "Ёмкость лимитера должна быть положительной.");
        _maxKeys = maxKeys;
    }

    /// <summary>Число отслеживаемых окон (диагностика и тесты памяти).</summary>
    public int ActiveKeyCount
    {
        get
        {
            lock (_gate)
                return _windows.Count;
        }
    }

    public RateLimitDecision TryConsume(string key, int limit, TimeSpan window, DateTime? now = null)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Лимит должен быть положительным.");
        if (window <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(window), "Окно должно быть положительным.");

        var current = now ?? DateTime.UtcNow;
        lock (_gate)
        {
            // Прореживание устаревших окон: пачками по счётчику и не чаще
            // PruneInterval при переполнении, чтобы отказ под нагрузкой не
            // превращался в O(keys) на каждый запрос.
            var sweepDue = ++_operations % PruneEveryOperations == 0
                || (_windows.Count >= _maxKeys
                    && (_lastPruneAt is null || current - _lastPruneAt >= PruneInterval));
            if (sweepDue)
            {
                RemoveExpiredLocked(current);
                _lastPruneAt = current;
            }

            if (_windows.TryGetValue(key, out var entry))
            {
                if (current < entry.ExpiresAt)
                {
                    if (entry.Count >= limit)
                        return RateLimitDecision.Deny(entry.ExpiresAt - current);
                    entry.Count++;
                    return RateLimitDecision.Allow;
                }

                _windows.Remove(key);
            }

            if (_windows.Count >= _maxKeys)
                return RateLimitDecision.Deny(TimeUntilNextExpiryLocked(current) ?? window);

            _windows[key] = new Window
            {
                ExpiresAt = current + window,
                Count = 1
            };
            return RateLimitDecision.Allow;
        }
    }

    /// <summary>Удаляет истёкшие окна; возвращает число удалённых.</summary>
    public int RemoveExpired(DateTime? now = null)
    {
        var current = now ?? DateTime.UtcNow;
        lock (_gate)
        {
            _lastPruneAt = current;
            return RemoveExpiredLocked(current);
        }
    }

    private int RemoveExpiredLocked(DateTime current)
    {
        if (_windows.Count == 0)
            return 0;

        List<string>? expired = null;
        foreach (var (key, entry) in _windows)
        {
            if (current >= entry.ExpiresAt)
                (expired ??= new List<string>()).Add(key);
        }

        if (expired is null)
            return 0;

        foreach (var key in expired)
            _windows.Remove(key);
        return expired.Count;
    }

    private TimeSpan? TimeUntilNextExpiryLocked(DateTime current)
    {
        TimeSpan? min = null;
        foreach (var entry in _windows.Values)
        {
            var remaining = entry.ExpiresAt - current;
            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;
            if (min is null || remaining < min)
                min = remaining;
        }
        return min;
    }

    private sealed class Window
    {
        public DateTime ExpiresAt;
        public int Count;
    }
}

/// <summary>Разрешение на запрос и срок до появления свободного окна.</summary>
public readonly record struct RateLimitDecision(bool Allowed, TimeSpan RetryAfter)
{
    public static RateLimitDecision Allow => new(true, TimeSpan.Zero);

    public static RateLimitDecision Deny(TimeSpan retryAfter) =>
        new(false, retryAfter <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : retryAfter);
}
