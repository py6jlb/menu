namespace MenuPlanner.Api.Observability;

/// <summary>
/// Настройки наблюдаемости. Экспорт совпадает с настроенными pipelines коллектора:
/// логи уходят по OTLP только когда задан endpoint; полные traces не хранятся
/// согласованно, поэтому их экспорт выключен по умолчанию и включается только
/// явным <c>OTEL_TRACES_EXPORT_ENABLED=true</c>. Activity/trace-id остаются для
/// корреляции журналов всегда.
/// </summary>
public sealed record ObservabilityOptions
{
    public const string OtlpEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";
    public const string TracesExportVariable = "OTEL_TRACES_EXPORT_ENABLED";
    public const string LogQueueSizeVariable = "OTEL_LOG_QUEUE_SIZE";
    public const string LogBatchSizeVariable = "OTEL_LOG_BATCH_SIZE";
    public const string ExportTimeoutVariable = "OTEL_EXPORT_TIMEOUT_MS";

    public const int DefaultLogQueueSize = 2048;
    public const int DefaultLogBatchSize = 512;
    public const int DefaultExportTimeoutMilliseconds = 10_000;
    public const int DefaultScheduledDelayMilliseconds = 5_000;
    public const int MinLogQueueSize = 256;
    public const int MinLogBatchSize = 1;
    public const int MinExportTimeoutMilliseconds = 1_000;
    public const int MaxExportTimeoutMilliseconds = 60_000;

    public string? OtlpEndpoint { get; init; }
    public bool TracesExportEnabled { get; init; }
    public int LogQueueSize { get; init; } = DefaultLogQueueSize;
    public int LogBatchSize { get; init; } = DefaultLogBatchSize;
    public int ExportTimeoutMilliseconds { get; init; } = DefaultExportTimeoutMilliseconds;
    public ReleaseIdentity Release { get; init; } = ReleaseIdentity.Unknown;
    public string? ManifestPath { get; init; }

    /// <summary>OTLP-приёмник задан — логи действительно экспортируются.</summary>
    public bool LogsExportEnabled => !string.IsNullOrWhiteSpace(OtlpEndpoint);

    public static ObservabilityOptions Read(IConfiguration configuration)
    {
        var endpoint = (configuration[OtlpEndpointVariable] ?? "").Trim();
        var manifestPath = (configuration[ReleaseIdentity.ManifestPathVariable] ?? "").Trim();

        return new ObservabilityOptions
        {
            OtlpEndpoint = endpoint.Length == 0 ? null : endpoint,
            TracesExportEnabled = ReadBool(configuration, TracesExportVariable, defaultValue: false),
            LogQueueSize = ReadInt(
                configuration, LogQueueSizeVariable, DefaultLogQueueSize, MinLogQueueSize, int.MaxValue),
            LogBatchSize = ReadInt(
                configuration, LogBatchSizeVariable, DefaultLogBatchSize, MinLogBatchSize, int.MaxValue),
            ExportTimeoutMilliseconds = ReadInt(
                configuration, ExportTimeoutVariable,
                DefaultExportTimeoutMilliseconds, MinExportTimeoutMilliseconds, MaxExportTimeoutMilliseconds),
            Release = ReleaseIdentity.Resolve(configuration),
            ManifestPath = manifestPath.Length == 0 ? null : manifestPath
        };
    }

    private static bool ReadBool(IConfiguration configuration, string variable, bool defaultValue)
    {
        var raw = (configuration[variable] ?? "").Trim();
        if (raw.Length == 0) return defaultValue;
        if (!bool.TryParse(raw, out var value))
            throw new InvalidOperationException(
                $"Конфигурация: {variable} должен быть true или false");
        return value;
    }

    private static int ReadInt(
        IConfiguration configuration, string variable, int defaultValue, int min, int max)
    {
        var raw = (configuration[variable] ?? "").Trim();
        if (raw.Length == 0) return defaultValue;
        if (!int.TryParse(raw, out var value) || value < min || value > max)
            throw new InvalidOperationException(
                $"Конфигурация: {variable} должен быть целым в диапазоне {min}..{max}");
        return value;
    }
}
