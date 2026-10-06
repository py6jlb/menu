using Xunit;
using MenuPlanner.Api.Observability;

namespace MenuPlanner.Api.Tests;

public sealed class SensitivePathTests
{
    [Theory]
    [InlineData("/api/shared/secret-token-123", "/api/shared/{token}")]
    [InlineData("/api/shared/secret-token-123/import", "/api/shared/{token}/import")]
    [InlineData("/r/secret-token-123", "/r/{token}")]
    [InlineData("/api/recipes/42", "/api/recipes/42")]
    [InlineData("/api/auth/verify", "/api/auth/verify")]
    [InlineData("/", "/")]
    public void Minimize_RedactsOnlyShareTokens(string path, string expected)
    {
        Assert.Equal(expected, SensitivePath.Minimize(path));
    }

    [Fact]
    public void ForLogging_PrefersRoutePattern_OverRawPath()
    {
        Assert.Equal("/api/shared/{token}", SensitivePath.ForLogging("/api/shared/{token}", "/api/shared/secret"));
    }

    [Fact]
    public void ForLogging_FallsBackToMinimizedPath()
    {
        Assert.Equal("/api/shared/{token}", SensitivePath.ForLogging(null, "/api/shared/secret"));
        Assert.Equal("/api/shared/{token}", SensitivePath.ForLogging("", "/api/shared/secret"));
    }
}
