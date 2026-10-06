using Microsoft.Extensions.Configuration;

namespace MenuPlanner.Api.Emails.Outbox;

/// <summary>
/// Параметры надёжной очереди писем: размер пачки, ограниченные повторы с
/// задержкой, срок аренды захвата, срок хранения окончательных записей и период
/// опроса фоновым воркером.
/// </summary>
public sealed class EmailOutboxOptions
{
    public const int DefaultMaxAttempts = 6;
    public const int DefaultBatchSize = 20;
    public const int DefaultRetryBaseDelaySeconds = 30;
    public const int DefaultMaxRetryDelayMinutes = 60;
    public const int DefaultLeaseSeconds = 120;
    public const int DefaultRetentionDays = 7;
    public const int DefaultPollSeconds = 15;

    public int MaxAttempts { get; set; } = DefaultMaxAttempts;
    public int BatchSize { get; set; } = DefaultBatchSize;
    public int RetryBaseDelaySeconds { get; set; } = DefaultRetryBaseDelaySeconds;
    public int MaxRetryDelayMinutes { get; set; } = DefaultMaxRetryDelayMinutes;
    public int LeaseSeconds { get; set; } = DefaultLeaseSeconds;
    public int RetentionDays { get; set; } = DefaultRetentionDays;
    public int PollSeconds { get; set; } = DefaultPollSeconds;

    /// <summary>Задержка перед повтором растёт экспоненциально и ограничена сверху.</summary>
    public TimeSpan DelayBefore(int attempt)
    {
        var safeAttempt = Math.Clamp(attempt, 1, 16);
        var seconds = (double)RetryBaseDelaySeconds * Math.Pow(2, safeAttempt - 1);
        var capped = Math.Min(seconds, MaxRetryDelayMinutes * 60d);
        return TimeSpan.FromSeconds(capped);
    }

    public static EmailOutboxOptions Read(IConfiguration configuration)
    {
        var options = new EmailOutboxOptions();
        options.MaxAttempts = Int(configuration, "EMAIL_OUTBOX_MAX_ATTEMPTS", options.MaxAttempts);
        options.BatchSize = Int(configuration, "EMAIL_OUTBOX_BATCH_SIZE", options.BatchSize);
        options.RetryBaseDelaySeconds = Int(
            configuration, "EMAIL_OUTBOX_RETRY_BASE_SECONDS", options.RetryBaseDelaySeconds);
        options.MaxRetryDelayMinutes = Int(
            configuration, "EMAIL_OUTBOX_MAX_DELAY_MINUTES", options.MaxRetryDelayMinutes);
        options.LeaseSeconds = Int(configuration, "EMAIL_OUTBOX_LEASE_SECONDS", options.LeaseSeconds);
        options.RetentionDays = Int(configuration, "EMAIL_OUTBOX_RETENTION_DAYS", options.RetentionDays);
        options.PollSeconds = Int(configuration, "EMAIL_OUTBOX_POLL_SECONDS", options.PollSeconds);
        options.Validate();
        return options;
    }

    public void Validate()
    {
        if (MaxAttempts < 1)
            throw new InvalidOperationException("Конфигурация: EMAIL_OUTBOX_MAX_ATTEMPTS должен быть ≥ 1");
        if (BatchSize < 1)
            throw new InvalidOperationException("Конфигурация: EMAIL_OUTBOX_BATCH_SIZE должен быть ≥ 1");
        if (RetryBaseDelaySeconds < 1)
            throw new InvalidOperationException(
                "Конфигурация: EMAIL_OUTBOX_RETRY_BASE_SECONDS должен быть ≥ 1");
        if (MaxRetryDelayMinutes < 1)
            throw new InvalidOperationException(
                "Конфигурация: EMAIL_OUTBOX_MAX_DELAY_MINUTES должен быть ≥ 1");
        if (LeaseSeconds < 1)
            throw new InvalidOperationException("Конфигурация: EMAIL_OUTBOX_LEASE_SECONDS должен быть ≥ 1");
        if (RetentionDays < 1)
            throw new InvalidOperationException("Конфигурация: EMAIL_OUTBOX_RETENTION_DAYS должен быть ≥ 1");
        if (PollSeconds < 1)
            throw new InvalidOperationException("Конфигурация: EMAIL_OUTBOX_POLL_SECONDS должен быть ≥ 1");
    }

    private static int Int(IConfiguration configuration, string key, int fallback) =>
        int.TryParse(configuration[key], out var value) ? value : fallback;
}
