namespace MenuPlanner.Api.Features.Settings;

/// <summary>Регистрация модуля личных настроек пользователя.</summary>
public static class SettingsServiceCollectionExtensions
{
    public static IServiceCollection AddSettings(this IServiceCollection services)
    {
        services.AddScoped<SettingsService>();
        return services;
    }
}
