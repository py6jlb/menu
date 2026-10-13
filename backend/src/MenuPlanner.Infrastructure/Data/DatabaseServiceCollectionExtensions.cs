using Microsoft.EntityFrameworkCore;

namespace MenuPlanner.Infrastructure.Data;

/// <summary>
/// Регистрация доступа к БД: строка подключения берётся из единого резолвера
/// <see cref="DatabaseConnection.Resolve"/>, чтобы рантайм и design-time фабрика
/// читали конфигурацию одинаково.
/// </summary>
public static class DatabaseServiceCollectionExtensions
{
    public static IServiceCollection AddDatabase(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = DatabaseConnection.Resolve(configuration);
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        return services;
    }
}
