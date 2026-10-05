using Xunit;
using MenuPlanner.Api.Auth;

namespace MenuPlanner.Api.Tests;

public sealed class RateLimiterTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    [Fact]
    public void TryConsume_AllowsExactlyLimit_WithinWindow_ThenBlocks()
    {
        var limiter = new FixedWindowRateLimiter();

        for (var i = 0; i < 5; i++)
            Assert.True(limiter.TryConsume("email:a@x.ru", 5, Hour, Now).Allowed);

        Assert.False(limiter.TryConsume("email:a@x.ru", 5, Hour, Now).Allowed);
    }

    [Fact]
    public void TryConsume_WindowRollsOver_AfterItsDuration()
    {
        var limiter = new FixedWindowRateLimiter();
        for (var i = 0; i < 5; i++)
            limiter.TryConsume("email:a@x.ru", 5, Hour, Now);

        Assert.False(limiter.TryConsume("email:a@x.ru", 5, Hour, Now.AddMinutes(59)).Allowed);

        Assert.True(limiter.TryConsume("email:a@x.ru", 5, Hour, Now.AddHours(1)).Allowed);
    }

    [Fact]
    public void TryConsume_KeysAreIsolated()
    {
        var limiter = new FixedWindowRateLimiter();
        for (var i = 0; i < 5; i++)
            limiter.TryConsume("ip:1.2.3.4", 5, Hour, Now);

        Assert.True(limiter.TryConsume("ip:5.6.7.8", 5, Hour, Now).Allowed);
    }

    [Fact]
    public void TryConsume_AtBoundary_TreatsExpiryAsNewWindow()
    {
        var limiter = new FixedWindowRateLimiter();
        Assert.True(limiter.TryConsume("k", 1, Hour, Now).Allowed);

        Assert.False(limiter.TryConsume("k", 1, Hour, Now.AddTicks(1)).Allowed);
        Assert.True(limiter.TryConsume("k", 1, Hour, Now.AddHours(1)).Allowed);
    }

    [Fact]
    public void TryConsume_WhenBlocked_ReturnsPositiveRetryAfter()
    {
        var limiter = new FixedWindowRateLimiter();
        limiter.TryConsume("k", 1, Hour, Now);

        var denied = limiter.TryConsume("k", 1, Hour, Now.AddMinutes(10));

        Assert.False(denied.Allowed);
        Assert.Equal(TimeSpan.FromMinutes(50), denied.RetryAfter);
    }

    [Fact]
    public void TryConsume_RejectsNewKeysWhenFull_ButKeepsTrackedOnes()
    {
        var limiter = new FixedWindowRateLimiter(maxKeys: 2);
        Assert.True(limiter.TryConsume("a", 5, Hour, Now).Allowed);
        Assert.True(limiter.TryConsume("b", 5, Hour, Now).Allowed);

        Assert.False(limiter.TryConsume("c", 5, Hour, Now).Allowed);
        Assert.Equal(2, limiter.ActiveKeyCount);

        // Уже отслеживаемый ключ продолжает работать и не вытесняется.
        Assert.True(limiter.TryConsume("a", 5, Hour, Now).Allowed);
        Assert.Equal(2, limiter.ActiveKeyCount);
    }

    [Fact]
    public void TryConsume_AtCapacity_FreesExpiredWindowsForNewKeys()
    {
        var limiter = new FixedWindowRateLimiter(maxKeys: 1);
        Assert.True(limiter.TryConsume("old", 5, Hour, Now).Allowed);
        Assert.False(limiter.TryConsume("new", 5, Hour, Now.AddMinutes(30)).Allowed);

        Assert.True(limiter.TryConsume("new", 5, Hour, Now.AddHours(1)).Allowed);
        Assert.Equal(1, limiter.ActiveKeyCount);
    }

    [Fact]
    public void RemoveExpired_DropsOnlyStaleWindows()
    {
        var limiter = new FixedWindowRateLimiter();
        limiter.TryConsume("old", 5, TimeSpan.FromMinutes(30), Now);
        limiter.TryConsume("fresh", 5, Hour, Now.AddMinutes(15));

        var removed = limiter.RemoveExpired(Now.AddMinutes(45));

        Assert.Equal(1, removed);
        Assert.Equal(1, limiter.ActiveKeyCount);
    }

    [Fact]
    public void TryConsume_IsThreadSafe_UnderContention()
    {
        var limiter = new FixedWindowRateLimiter();
        const int limit = 50;
        var allowed = 0;

        Parallel.For(0, 500, _ =>
        {
            if (limiter.TryConsume("shared", limit, Hour, Now).Allowed)
                Interlocked.Increment(ref allowed);
        });

        Assert.Equal(limit, allowed);
    }

    [Fact]
    public void UniqueEmailStream_KeepsMemoryBounded()
    {
        var limiter = new FixedWindowRateLimiter(maxKeys: 100);

        for (var i = 0; i < 10_000; i++)
            limiter.TryConsume($"email:{i}@x.ru", 1, Hour, Now);

        Assert.True(limiter.ActiveKeyCount <= 100);
    }
}
