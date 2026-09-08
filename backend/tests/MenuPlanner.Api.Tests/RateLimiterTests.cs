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
            Assert.True(limiter.TryConsume("email:a@x.ru", 5, Hour, Now));

        Assert.False(limiter.TryConsume("email:a@x.ru", 5, Hour, Now));
    }

    [Fact]
    public void TryConsume_WindowRollsOver_AfterItsDuration()
    {
        var limiter = new FixedWindowRateLimiter();
        for (var i = 0; i < 5; i++)
            limiter.TryConsume("email:a@x.ru", 5, Hour, Now);

        Assert.False(limiter.TryConsume("email:a@x.ru", 5, Hour, Now.AddMinutes(59)));

        Assert.True(limiter.TryConsume("email:a@x.ru", 5, Hour, Now.AddHours(1)));
    }

    [Fact]
    public void TryConsume_KeysAreIsolated()
    {
        var limiter = new FixedWindowRateLimiter();
        for (var i = 0; i < 5; i++)
            limiter.TryConsume("ip:1.2.3.4", 5, Hour, Now);

        Assert.True(limiter.TryConsume("ip:5.6.7.8", 5, Hour, Now));
    }
}
