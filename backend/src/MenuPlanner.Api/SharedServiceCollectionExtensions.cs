using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MenuPlanner.Api;

/// <summary>
/// Общие host-регистрации, общие для нескольких модулей. <see cref="TimeProvider"/>
/// абстрагирует время, чтобы тесты могли подменять часы без правки домена.
/// </summary>
public static class SharedServiceCollectionExtensions
{
    public static IServiceCollection AddShared(this IServiceCollection services)
    {
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }
}
