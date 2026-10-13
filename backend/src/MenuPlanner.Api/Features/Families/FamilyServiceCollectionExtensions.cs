namespace MenuPlanner.Api.Features.Families;

/// <summary>Регистрация модуля семьи: создание, вступление и управление составом.</summary>
public static class FamilyServiceCollectionExtensions
{
    public static IServiceCollection AddFamilies(this IServiceCollection services)
    {
        services.AddScoped<FamilyService>();
        return services;
    }
}
