namespace MenuPlanner.Api.Auth;

public sealed class FixedWindowRateLimiter
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Window> _windows = new();

    public bool TryConsume(string key, int limit, TimeSpan window, DateTime? now = null)
    {
        var current = now ?? DateTime.UtcNow;
        lock (_gate)
        {
            if (!_windows.TryGetValue(key, out var entry) || current - entry.StartedAt >= window)
            {
                _windows[key] = new Window { StartedAt = current, Count = 1 };
                return true;
            }

            if (entry.Count >= limit)
                return false;

            entry.Count++;
            return true;
        }
    }

    private sealed class Window
    {
        public DateTime StartedAt;
        public int Count;
    }
}
