using Microsoft.Extensions.Configuration;
using Xunit;
using MenuPlanner.Api.Configuration;

namespace MenuPlanner.Api.Tests;

public sealed class ProductionConfigurationTests
{
    private const string StrongJwt = "N7q2Zx9Kp4Rt6Vw8Yb3Mc5Ld1Fh0Jg2S";

    private static IConfiguration ProductionBase(params (string Key, string? Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["DEPLOYMENT_MODE"] = "production",
            ["PUBLIC_BASE_URL"] = "https://menu.example.com",
            ["SMTP_HOST"] = "smtp.example.com",
            ["SMTP_FROM"] = "noreply@example.com"
        };
        foreach (var pair in overrides) values[pair.Key] = pair.Value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void Production_WithCompleteConfiguration_IsAccepted()
    {
        var options = ProductionConfiguration.Read(ProductionBase(), StrongJwt);

        Assert.Equal(DeploymentOptions.ProductionMode, options.Mode);
        Assert.True(options.IsProduction);
    }

    [Fact]
    public void UnsetMode_DefaultsToProduction_AndRequiresHttps()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["JWT_SECRET"] = StrongJwt }).Build();

        var error = Assert.Throws<InvalidOperationException>(
            () => ProductionConfiguration.Read(configuration, StrongJwt));
        Assert.Contains("PUBLIC_BASE_URL", error.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://menu.example.com")]
    [InlineData("menu.example.com")]
    [InlineData("ftp://menu.example.com")]
    public void Production_WithoutHttpsPublicUrl_IsRejected(string? publicUrl)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => ProductionConfiguration.Read(
                ProductionBase(("PUBLIC_BASE_URL", publicUrl)), StrongJwt));
        Assert.Contains("PUBLIC_BASE_URL", error.Message);
        Assert.Contains("lab", error.Message);
    }

    [Fact]
    public void Production_WithoutMail_IsRejected()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => ProductionConfiguration.Read(
                ProductionBase(("SMTP_HOST", "")), StrongJwt));
        Assert.Contains("SMTP_HOST", error.Message);
        Assert.Contains("SMTP_FROM", error.Message);
    }

    [Fact]
    public void Production_WithoutFrom_IsRejected()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => ProductionConfiguration.Read(
                ProductionBase(("SMTP_FROM", "")), StrongJwt));
        Assert.Contains("SMTP_FROM", error.Message);
    }

    [Theory]
    [InlineData("dev-only-secret-change-me-in-production-0123456789abcdef")]
    [InlineData("change-me-min-32-chars-0123456789abcdef")]
    [InlineData("this-is-a-placeholder-secret-value-000")]
    [InlineData("test-secret-test-secret-test-secret")]
    public void Production_WithKnownPlaceholderJwtSecret_IsRejected_WithoutRevealingValue(string secret)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => ProductionConfiguration.Read(ProductionBase(), secret));

        Assert.Contains("JWT_SECRET", error.Message);
        Assert.DoesNotContain(secret, error.Message);
    }

    [Fact]
    public void Production_WithPlaceholderDbPassword_IsRejected_WithoutRevealingValue()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["DEPLOYMENT_MODE"] = "production",
                ["PUBLIC_BASE_URL"] = "https://menu.example.com",
                ["SMTP_HOST"] = "smtp.example.com",
                ["SMTP_FROM"] = "noreply@example.com",
                ["DB_HOST"] = "db",
                ["DB_PASSWORD"] = "change-me-strong"
            }).Build();

        var error = Assert.Throws<InvalidOperationException>(
            () => ProductionConfiguration.Read(configuration, StrongJwt));
        Assert.Contains("DB_PASSWORD", error.Message);
        Assert.DoesNotContain("change-me-strong", error.Message);
    }

    [Fact]
    public void LabMode_ExplicitlyAllowsHttpAndLoggingMail()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["DEPLOYMENT_MODE"] = "lab",
                ["SMTP_HOST"] = "",
                ["JWT_SECRET"] = "dev-only-secret-change-me-in-production-0123456789abcdef"
            }).Build();

        var options = ProductionConfiguration.Read(
            configuration, "dev-only-secret-change-me-in-production-0123456789abcdef");

        Assert.True(options.IsLab);
    }

    [Fact]
    public void UnknownMode_IsRejected()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["DEPLOYMENT_MODE"] = "staging" }).Build();

        var error = Assert.Throws<InvalidOperationException>(
            () => ProductionConfiguration.Read(configuration, StrongJwt));
        Assert.Contains("DEPLOYMENT_MODE", error.Message);
    }

    [Fact]
    public void Factory_ProductionWithoutHttps_FailsFast_WithClearMessage()
    {
        using var factory = new ApiFactory();
        factory.Settings["DEPLOYMENT_MODE"] = "production";
        factory.Settings["SMTP_HOST"] = "smtp.example.com";
        factory.Settings["SMTP_FROM"] = "noreply@example.com";
        factory.Settings["JWT_SECRET"] = StrongJwt;
        factory.Settings["PUBLIC_BASE_URL"] = "";

        var error = Assert.ThrowsAny<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("PUBLIC_BASE_URL", Flatten(error));
    }

    [Fact]
    public void Factory_ProductionWithPlaceholderJwt_FailsFast_WithoutRevealingSecret()
    {
        const string placeholder = "dev-only-secret-change-me-in-production-0123456789abcdef";
        using var factory = new ApiFactory();
        factory.Settings["DEPLOYMENT_MODE"] = "production";
        factory.Settings["PUBLIC_BASE_URL"] = "https://menu.example.com";
        factory.Settings["SMTP_HOST"] = "smtp.example.com";
        factory.Settings["SMTP_FROM"] = "noreply@example.com";
        factory.Settings["JWT_SECRET"] = placeholder;

        var error = Assert.ThrowsAny<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("JWT_SECRET", Flatten(error));
        Assert.DoesNotContain(placeholder, Flatten(error));
    }

    [Fact]
    public async Task Factory_ProductionWithCompleteConfiguration_Starts()
    {
        using var factory = new ApiFactory();
        factory.Settings["DEPLOYMENT_MODE"] = "production";
        factory.Settings["PUBLIC_BASE_URL"] = "https://menu.example.com";
        factory.Settings["SMTP_HOST"] = "smtp.example.com";
        factory.Settings["SMTP_FROM"] = "noreply@example.com";
        factory.Settings["JWT_SECRET"] = StrongJwt;

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health");
        response.EnsureSuccessStatusCode();
    }

    private static string Flatten(Exception exception)
    {
        var text = exception.Message;
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
            text += " | " + inner.Message;
        return text;
    }
}
