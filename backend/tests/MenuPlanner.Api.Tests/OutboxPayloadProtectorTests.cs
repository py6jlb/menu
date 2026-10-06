using System.Security.Cryptography;
using Xunit;
using MenuPlanner.Api.Emails.Outbox;

namespace MenuPlanner.Api.Tests;

public sealed class OutboxPayloadProtectorTests
{
    [Fact]
    public void Protect_Unprotect_RoundTrips()
    {
        var protector = TestOutbox.Protector();

        var payload = protector.Protect("123456");

        Assert.NotEqual("123456", payload);
        Assert.DoesNotContain("123456", payload);
        Assert.True(protector.TryUnprotect(payload, out var plain));
        Assert.Equal("123456", plain);
    }

    [Fact]
    public void Protect_UsesRandomNonce_SoEqualPlaintextsDiffer()
    {
        var protector = TestOutbox.Protector();

        Assert.NotEqual(protector.Protect("000000"), protector.Protect("000000"));
    }

    [Fact]
    public void TryUnprotect_WithDifferentKey_FailsWithoutThrowing()
    {
        var protector = TestOutbox.Protector();
        var other = new AesGcmOutboxPayloadProtector(
            SHA256.HashData("another-key"u8.ToArray()));

        var payload = protector.Protect("123456");

        Assert.False(other.TryUnprotect(payload, out _));
    }

    [Fact]
    public void TryUnprotect_WithTamperedPayload_Fails()
    {
        var protector = TestOutbox.Protector();
        var payload = protector.Protect("123456");
        var bytes = Convert.FromBase64String(payload);
        bytes[^1] ^= 0xFF;

        Assert.False(protector.TryUnprotect(Convert.ToBase64String(bytes), out _));
    }
}
