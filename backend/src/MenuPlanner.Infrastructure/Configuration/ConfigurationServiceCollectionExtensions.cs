namespace MenuPlanner.Infrastructure.Configuration;

/// <summary>
/// Регистрация проверки production-конфигурации: до публичной работы хост должен
/// знать, что запущен в production с реальными секретами и настроенной почтой.
/// </summary>
public static class ConfigurationServiceCollectionExtensions
{
    public static IServiceCollection AddProductionConfiguration(
        this IServiceCollection services, IConfiguration configuration, string jwtSecret)
    {
        services.AddSingleton(ProductionConfiguration.Read(configuration, jwtSecret));
        return services;
    }
}
