using MenuPlanner.Api.Features.Auth.Accounts;
using MenuPlanner.Api.Features.Auth.Admin;
using MenuPlanner.Api.Features.Auth.PasswordReset;
using MenuPlanner.Api.Features.Auth.Verification;
using MenuPlanner.Api.Features.Families;
using MenuPlanner.Api.Features.Ingredients;
using MenuPlanner.Api.Features.Plans;
using MenuPlanner.Api.Features.Recipes.Crud;
using MenuPlanner.Api.Features.Recipes.Sharing;
using MenuPlanner.Api.Features.Settings;
using MenuPlanner.Api.Features.ShoppingList;
using MenuPlanner.Infrastructure.Configuration;
using MenuPlanner.Infrastructure.Health;
using MenuPlanner.Infrastructure.Observability;
using Microsoft.AspNetCore.Mvc;

namespace MenuPlanner.Api;

/// <summary>
/// Host-расширения `Program.cs`: подключение middleware доверенных прокси,
/// карта всех маршрутов и применение миграций БД.
/// </summary>
public static class HostExtensions
{
    /// <summary>
    /// X-Forwarded-* принимаются только когда задан доверенный прокси. Без него
    /// middleware не подключается вовсе: пустой список известных прокси в ASP.NET
    /// Core трактуется как «доверять всем», что позволило бы подделать IP.
    /// </summary>
    public static WebApplication UseTrustedForwardedHeaders(this WebApplication app)
    {
        var forwardedHeaders = ForwardedHeaderConfiguration.Build(app.Configuration);
        if (ForwardedHeaderConfiguration.HasTrustedProxies(forwardedHeaders))
            app.UseForwardedHeaders(forwardedHeaders);
        return app;
    }

    /// <summary>
    /// Карта HTTP-маршрутов: дешёвый liveness/readiness и все фичевые эндпоинты.
    /// Состав и порядок маршрутов намеренно видны в одном месте.
    /// </summary>
    public static WebApplication MapFeatureEndpoints(this WebApplication app)
    {
        var releaseId = app.Services.GetRequiredService<ObservabilityOptions>().Release.Id;

        app.MapGet("/health", () => Results.Json(
            new { status = "ok", service = "menu-planner-api", release = releaseId }));

        app.MapGet("/ready", GetReadinessAsync);

        static Task<IResult> GetReadinessAsync(
            [FromServices] IDatabaseReadinessProbe probe,
            [FromServices] ReadinessOptions options,
            [FromServices] ILoggerFactory loggerFactory,
            CancellationToken requestToken)
            => ReadinessEndpoint.GetReadinessAsync(probe, options, loggerFactory, requestToken);

        app.MapAuthEndpoints();
        app.MapEmailVerificationEndpoints();
        app.MapPasswordResetEndpoints();
        app.MapAdminEndpoints();
        app.MapFamilyEndpoints();
        app.MapRecipeEndpoints();
        app.MapRecipeShareEndpoints();
        app.MapSharedRecipeEndpoints();
        app.MapIngredientEndpoints();
        app.MapPlanEndpoints();
        app.MapSettingsEndpoints();
        app.MapShoppingListEndpoints();
        return app;
    }
}
