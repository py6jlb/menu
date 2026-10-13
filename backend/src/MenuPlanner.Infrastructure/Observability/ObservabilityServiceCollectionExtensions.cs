using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace MenuPlanner.Infrastructure.Observability;

/// <summary>
/// Регистрация наблюдаемости. Activity и trace-id нужны для корреляции журналов
/// всегда; полные traces не хранятся согласованно, поэтому их OTLP-экспорт
/// выключен, пока не появится согласованное хранилище, и включается явным флагом.
/// </summary>
public static class ObservabilityServiceCollectionExtensions
{
    public static WebApplicationBuilder AddObservability(
        this WebApplicationBuilder builder, IConfiguration configuration)
    {
        var observability = ObservabilityOptions.Read(configuration);
        builder.Services.AddSingleton(observability);

        var otelResource = AddReleaseResource(ResourceBuilder.CreateDefault(), observability.Release.Id);

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => AddReleaseResource(resource, observability.Release.Id))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation();
                if (observability.TracesExportEnabled) tracing.AddOtlpExporter();
            });

        if (observability.LogsExportEnabled)
        {
            builder.Logging.AddOpenTelemetry(options =>
            {
                options.SetResourceBuilder(otelResource);
                options.IncludeScopes = true;
                options.IncludeFormattedMessage = true;
                // Ограниченная очередь не даёт памяти расти при недоступном Collector,
                // а экспорт идёт фоном и не блокирует пользовательский запрос.
                options.AddOtlpExporter((exporter, processor) =>
                {
                    exporter.TimeoutMilliseconds = observability.ExportTimeoutMilliseconds;
                    processor.ExportProcessorType = ExportProcessorType.Batch;
                    processor.BatchExportProcessorOptions.MaxQueueSize = observability.LogQueueSize;
                    processor.BatchExportProcessorOptions.MaxExportBatchSize = observability.LogBatchSize;
                    processor.BatchExportProcessorOptions.ExporterTimeoutMilliseconds =
                        observability.ExportTimeoutMilliseconds;
                    processor.BatchExportProcessorOptions.ScheduledDelayMilliseconds =
                        ObservabilityOptions.DefaultScheduledDelayMilliseconds;
                });
            });
        }

        return builder;
    }

    private static ResourceBuilder AddReleaseResource(ResourceBuilder resource, string releaseId) =>
        resource
            .AddService("menu-planner-api", serviceVersion: releaseId)
            .AddAttributes(new[] { new KeyValuePair<string, object>("release.id", releaseId) });
}
