namespace MenuPlanner.Api.Features.Plans;

/// <summary>Регистрация модуля недельного плана: сохранение и чтение планов.</summary>
public static class PlanServiceCollectionExtensions
{
    public static IServiceCollection AddPlans(this IServiceCollection services)
    {
        services.AddScoped<WeekPlanSaver>();
        services.AddScoped<WeekPlanReader>();
        return services;
    }
}
