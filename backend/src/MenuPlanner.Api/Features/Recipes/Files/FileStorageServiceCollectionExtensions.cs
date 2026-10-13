namespace MenuPlanner.Api.Features.Recipes.Files;

/// <summary>
/// Регистрация единого модуля управляемых файлов рецепта (фото и PDF): хранилище,
/// жизненный цикл, безопасная уборка и параметры её фонового воркера.
/// </summary>
public static class FileStorageServiceCollectionExtensions
{
    public static IServiceCollection AddFileStorage(this IServiceCollection services)
    {
        services.AddSingleton<IRecipeFileStore, FileStorage>();
        services.AddScoped<RecipeFileLifecycle>();
        services.AddScoped<RecipeGarbageCollector>();
        services.AddSingleton(provider => RecipeFileCleanupOptions.Read(
            provider.GetRequiredService<IConfiguration>(),
            provider.GetRequiredService<ILogger<RecipeFileCleanupOptions>>()));
        services.AddHostedService<RecipeFileCleanupWorker>();
        return services;
    }
}
