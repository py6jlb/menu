using MenuPlanner.Api.Features.Recipes.Crud;
using MenuPlanner.Api.Features.Recipes.External;
using MenuPlanner.Api.Features.Recipes.Reading;
using MenuPlanner.Api.Features.Recipes.Repetition;
using MenuPlanner.Api.Features.Recipes.Sharing;

namespace MenuPlanner.Api.Features.Recipes;

/// <summary>
/// Регистрация модуля рецептов: чтение и ревизии, мутации, шаринг и импорт
/// внешних рецептов, повторяемость и настройки публичных ссылок.
/// </summary>
public static class RecipeServiceCollectionExtensions
{
    public static IServiceCollection AddRecipes(this IServiceCollection services)
    {
        services.AddScoped<SourceFamilyNameResolver>();
        services.AddScoped<ExternalRecipeSourceLoader>();
        services.AddScoped<ExternalRecipeStateResolver>();
        services.AddScoped<ExternalRecipeNameCache>();
        services.AddScoped<ExternalRecipeContext>();
        services.AddScoped<RecipeReader>();
        services.AddScoped<RecipeRevisionReader>();
        services.AddScoped<ExternalRecipePromotionService>();
        services.AddScoped<RecipeMutationService>();
        services.AddScoped<RecipeSharingService>();
        services.AddScoped<SharedRecipeImportService>();
        services.AddScoped<RepetitionCounter>();

        services.AddSingleton(provider => new ShareOptions
        {
            BaseUrl = provider.GetRequiredService<IConfiguration>()["SHARE_BASE_URL"] ?? ""
        });
        return services;
    }
}
