using Microsoft.Extensions.Configuration;
using Xunit;
using MenuPlanner.Api.Observability;

namespace MenuPlanner.Api.Tests;

public sealed class ObservabilityConfigurationTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] pairs)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var pair in pairs) values[pair.Key] = pair.Value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void Logs_WithoutOtlpEndpoint_AreNotExported()
    {
        var options = ObservabilityOptions.Read(Config());

        Assert.Null(options.OtlpEndpoint);
        Assert.False(options.LogsExportEnabled);
    }

    [Fact]
    public void Logs_WithOtlpEndpoint_AreExported()
    {
        var options = ObservabilityOptions.Read(
            Config((ObservabilityOptions.OtlpEndpointVariable, "http://otel-collector:4317")));

        Assert.True(options.LogsExportEnabled);
    }

    [Fact]
    public void Traces_AreNotExported_ByDefault_EvenWithEndpoint()
    {
        // Пока нет согласованного хранения полных traces, их экспорт выключен.
        var options = ObservabilityOptions.Read(
            Config(
                (ObservabilityOptions.OtlpEndpointVariable, "http://otel-collector:4317"),
                (ObservabilityOptions.TracesExportVariable, null)));

        Assert.True(options.LogsExportEnabled);
        Assert.False(options.TracesExportEnabled);
    }

    [Fact]
    public void Traces_Export_RequiresExplicitOptIn()
    {
        var options = ObservabilityOptions.Read(
            Config(
                (ObservabilityOptions.OtlpEndpointVariable, "http://otel-collector:4317"),
                (ObservabilityOptions.TracesExportVariable, "true")));

        Assert.True(options.TracesExportEnabled);
    }

    [Fact]
    public void QueueBatchAndTimeout_HaveBoundedDefaults()
    {
        var options = ObservabilityOptions.Read(Config());

        Assert.Equal(ObservabilityOptions.DefaultLogQueueSize, options.LogQueueSize);
        Assert.Equal(ObservabilityOptions.DefaultLogBatchSize, options.LogBatchSize);
        Assert.Equal(
            ObservabilityOptions.DefaultExportTimeoutMilliseconds, options.ExportTimeoutMilliseconds);
        Assert.True(options.LogQueueSize <= 4096);
        Assert.True(options.LogBatchSize <= options.LogQueueSize);
        Assert.InRange(
            options.ExportTimeoutMilliseconds,
            ObservabilityOptions.MinExportTimeoutMilliseconds,
            ObservabilityOptions.MaxExportTimeoutMilliseconds);
    }

    [Fact]
    public void QueueBatchAndTimeout_AcceptOverrides()
    {
        var options = ObservabilityOptions.Read(Config(
            (ObservabilityOptions.LogQueueSizeVariable, "1024"),
            (ObservabilityOptions.LogBatchSizeVariable, "128"),
            (ObservabilityOptions.ExportTimeoutVariable, "3000")));

        Assert.Equal(1024, options.LogQueueSize);
        Assert.Equal(128, options.LogBatchSize);
        Assert.Equal(3000, options.ExportTimeoutMilliseconds);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("10")]
    [InlineData("-5")]
    [InlineData("not-a-number")]
    public void LogQueueSize_OutOfRange_IsRejected(string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => ObservabilityOptions.Read(
            Config((ObservabilityOptions.LogQueueSizeVariable, value))));

        Assert.Contains(ObservabilityOptions.LogQueueSizeVariable, error.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("999999")]
    [InlineData("nope")]
    public void ExportTimeout_OutOfRange_IsRejected(string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => ObservabilityOptions.Read(
            Config((ObservabilityOptions.ExportTimeoutVariable, value))));

        Assert.Contains(ObservabilityOptions.ExportTimeoutVariable, error.Message);
    }

    [Fact]
    public void InvalidTracesFlag_IsRejected()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ObservabilityOptions.Read(
            Config((ObservabilityOptions.TracesExportVariable, "maybe"))));

        Assert.Contains(ObservabilityOptions.TracesExportVariable, error.Message);
    }
}
