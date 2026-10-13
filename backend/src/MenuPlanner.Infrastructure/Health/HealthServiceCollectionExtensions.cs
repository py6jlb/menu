namespace MenuPlanner.Infrastructure.Health;

/// <summary>
/// Регистрация readiness-проверки БД. Проверка идёт с ограниченным ожиданием и
/// не раскрывает секретов ни в ответе, ни в журнале.
/// </summary>
public static class HealthServiceCollectionExtensions
{
    public static IServiceCollection AddReadiness(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(ReadinessOptions.FromConfiguration(configuration));
        services.AddScoped<IDatabaseReadinessProbe, DatabaseReadinessProbe>();
        return services;
    }
}
