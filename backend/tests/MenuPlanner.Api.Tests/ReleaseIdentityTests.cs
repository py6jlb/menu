using Microsoft.Extensions.Configuration;
using Xunit;
using MenuPlanner.Api.Observability;

namespace MenuPlanner.Api.Tests;

public sealed class ReleaseIdentityTests : IDisposable
{
    private readonly string _manifest = Path.Combine(
        Path.GetTempPath(), "menu-release-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        if (File.Exists(_manifest)) File.Delete(_manifest);
    }

    private static IConfiguration Config(params (string Key, string? Value)[] pairs)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var pair in pairs) values[pair.Key] = pair.Value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void EnvironmentId_TakesPrecedence()
    {
        File.WriteAllText(_manifest, "{\"commit\":\"from-manifest\"}");

        var release = ReleaseIdentity.Resolve(Config(
            (ReleaseIdentity.IdVariable, "from-env"),
            (ReleaseIdentity.ManifestPathVariable, _manifest)));

        Assert.Equal("from-env", release.Id);
        Assert.Equal(ReleaseIdentitySource.Environment, release.Source);
        Assert.True(release.IsKnown);
    }

    [Fact]
    public void ManifestCommit_IsRead_WhenNoEnvironmentId()
    {
        File.WriteAllText(_manifest, "{\"commit\":\"abc123def\",\"tag\":\"abc123\"}");

        var release = ReleaseIdentity.Resolve(Config(
            (ReleaseIdentity.ManifestPathVariable, _manifest)));

        Assert.Equal("abc123def", release.Id);
        Assert.Equal(ReleaseIdentitySource.Manifest, release.Source);
    }

    [Fact]
    public void ManifestTag_IsFallback_WhenCommitMissing()
    {
        File.WriteAllText(_manifest, "{\"tag\":\"abc123\"}");

        var release = ReleaseIdentity.Resolve(Config(
            (ReleaseIdentity.ManifestPathVariable, _manifest)));

        Assert.Equal("abc123", release.Id);
    }

    [Fact]
    public void MissingManifest_DoesNotThrow_AndYieldsUnknown()
    {
        var missing = Path.Combine(Path.GetTempPath(), "menu-missing-" + Guid.NewGuid().ToString("N"));

        var release = ReleaseIdentity.Resolve(Config(
            (ReleaseIdentity.ManifestPathVariable, missing)));

        Assert.False(release.IsKnown);
        Assert.Equal(ReleaseIdentity.UnknownId, release.Id);
        Assert.Equal(ReleaseIdentitySource.Unavailable, release.Source);
    }

    [Fact]
    public void BrokenManifest_DoesNotThrow_AndYieldsUnknown()
    {
        File.WriteAllText(_manifest, "{not-json");

        var release = ReleaseIdentity.Resolve(Config(
            (ReleaseIdentity.ManifestPathVariable, _manifest)));

        Assert.Equal(ReleaseIdentitySource.Unavailable, release.Source);
    }

    [Fact]
    public void UnsafeIdentifier_IsRejected()
    {
        var release = ReleaseIdentity.Resolve(Config(
            (ReleaseIdentity.IdVariable, "bad id\nwith control")));

        Assert.False(release.IsKnown);
    }

    [Fact]
    public void MutableLatestTag_IsNotAcceptedAsRelease()
    {
        var release = ReleaseIdentity.Resolve(Config(
            (ReleaseIdentity.IdVariable, ReleaseIdentity.MutableTag)));

        Assert.False(release.IsKnown);
        Assert.Equal(ReleaseIdentity.UnknownId, release.Id);
    }

    [Fact]
    public void NoConfigurationAtAll_YieldsUnknown()
    {
        var release = ReleaseIdentity.Resolve(Config());

        Assert.Equal(ReleaseIdentity.UnknownId, release.Id);
        Assert.False(release.IsKnown);
    }
}
