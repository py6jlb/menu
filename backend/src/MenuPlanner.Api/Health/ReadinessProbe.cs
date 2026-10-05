using MenuPlanner.Api.Data;

namespace MenuPlanner.Api.Health;

/// <summary>
/// Проверка доступности БД для readiness. Может бросить исключение провайдера;
/// вызывающая сторона (endpoint) ограничивает ожидание и не раскрывает детали.
/// </summary>
public interface IDatabaseReadinessProbe
{
    Task<bool> CanConnectAsync(CancellationToken cancellationToken);
}

public sealed class DatabaseReadinessProbe : IDatabaseReadinessProbe
{
    private readonly AppDbContext _db;

    public DatabaseReadinessProbe(AppDbContext db) => _db = db;

    public Task<bool> CanConnectAsync(CancellationToken cancellationToken)
        => _db.Database.CanConnectAsync(cancellationToken);
}

/// <summary>
/// Ограниченное время ожидания readiness. Секреты сюда не входят и не логируются.
/// </summary>
public sealed record ReadinessOptions(TimeSpan Timeout)
{
    public const int DefaultTimeoutSeconds = 3;
    public const int MinTimeoutSeconds = 1;
    public const int MaxTimeoutSeconds = 30;

    public static ReadinessOptions FromConfiguration(IConfiguration configuration)
    {
        var seconds = DefaultTimeoutSeconds;
        if (int.TryParse(configuration["READINESS_TIMEOUT_SECONDS"], out var configured))
            seconds = Math.Clamp(configured, MinTimeoutSeconds, MaxTimeoutSeconds);

        return new ReadinessOptions(TimeSpan.FromSeconds(seconds));
    }
}
