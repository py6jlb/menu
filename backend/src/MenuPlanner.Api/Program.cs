using MenuPlanner.Api;
using MenuPlanner.Api.Features.Auth;
using MenuPlanner.Api.Features.Auth.Admin;
using MenuPlanner.Api.Features.Auth.Sessions;
using MenuPlanner.Api.Features.Families;
using MenuPlanner.Api.Features.Plans;
using MenuPlanner.Api.Features.Recipes;
using MenuPlanner.Api.Features.Recipes.Files;
using MenuPlanner.Api.Features.Settings;
using MenuPlanner.Infrastructure.Configuration;
using MenuPlanner.Infrastructure.Data;
using MenuPlanner.Infrastructure.Emails;
using MenuPlanner.Infrastructure.Health;
using MenuPlanner.Infrastructure.Observability;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// Composition-root: каждый модуль регистрирует свои сервисы рядом с собой.
builder.Services
    .AddDatabase(configuration)
    .AddFileStorage()
    .AddReadiness(configuration)
    // Секрет JWT нужен до сборки хоста: из него проверяется production-конфигурация.
    .AddProductionConfiguration(configuration, JwtOptions.Read(configuration).Secret)
    .AddJwtAuthentication(configuration)
    .AddEmailDelivery(configuration)
    .AddAuth(configuration)
    .AddFamilies()
    .AddRecipes()
    .AddPlans()
    .AddSettings()
    .AddShared();

builder.AddObservability(configuration);

var app = builder.Build();

// Закрытая операторская команда назначения первоначального администратора.
// Выполняется вне публичного HTTP-маршрута и завершает процесс до старта
// веб-сервера; применяет миграции и читает пароль скрыто из stdin.
if (AdminBootstrapArgs.IsCommand(args))
{
    Environment.ExitCode = await AdminBootstrapCommand.RunAsync(app.Services, args);
    return;
}

// X-Forwarded-* принимаются только когда задан доверенный прокси.
app.UseTrustedForwardedHeaders();

// Одна структурированная запись на запрос (operation/trace-id/release-id);
// оборачивает обработчик сбоев, поэтому пишется и для успешных, и для ошибочных.
app.UseRequestLogging();

// Неожиданный сбой отдаётся одним JSON-ответом с trace-id, без stack trace/SQL.
app.UseApiExceptionHandling();

var observability = app.Services.GetRequiredService<ObservabilityOptions>();
var startupLogger = app.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("MenuPlanner.Startup");
startupLogger.LogInformation(
    "Старт menu-planner-api. ReleaseId={ReleaseId} ReleaseSource={ReleaseSource}",
    observability.Release.Id,
    observability.Release.Source);
if (!observability.Release.IsKnown)
    startupLogger.LogWarning(
        "Release-id недоступен: RELEASE_ID не задан и manifest не прочитан (путь: {ManifestPath}). ReleaseId={ReleaseId}",
        observability.ManifestPath ?? "(не задан)",
        observability.Release.Id);

app.Services.GetRequiredService<IRecipeFileStore>();

app.MapFeatureEndpoints();

// Схема БД ведётся миграциями и применяется на старте (для реляционных провайдеров).
await app.UseDatabaseMigration();

app.Run();

public partial class Program { }
