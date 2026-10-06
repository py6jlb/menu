using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Auth.Codes;
using MenuPlanner.Api.Configuration;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Emails;
using MenuPlanner.Api.Emails.Outbox;
using MenuPlanner.Api.Families;
using MenuPlanner.Api.Health;
using MenuPlanner.Api.Ingredients;
using MenuPlanner.Api.Plans;
using MenuPlanner.Api.Recipes;
using MenuPlanner.Api.Recipes.External;
using MenuPlanner.Api.Recipes.Repetition;
using MenuPlanner.Api.Settings;
using MenuPlanner.Api.ShoppingList;

var builder = WebApplication.CreateBuilder(args);

var connectionString = DatabaseConnection.Resolve(builder.Configuration);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<PhotoStorage>();

builder.Services.AddSingleton(ReadinessOptions.FromConfiguration(builder.Configuration));
builder.Services.AddScoped<IDatabaseReadinessProbe, DatabaseReadinessProbe>();

var jwtOptions = ReadJwtOptions(builder.Configuration);
var deploymentOptions = ProductionConfiguration.Read(builder.Configuration, jwtOptions.Secret);
builder.Services.AddSingleton(deploymentOptions);
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<JwtTokenService>();

// ProductionConfiguration уже отклонил production+log на старте; здесь транспорт
// выбирается по явному режиму, без автоматического падения в логирование.
var emailOptions = EmailConfiguration.Read(builder.Configuration);
builder.Services.AddSingleton(emailOptions);
builder.Services.AddSingleton<IEmailTransport>(services =>
    emailOptions.Transport == EmailTransportMode.Log
        ? new LoggingEmailTransport(services.GetRequiredService<ILogger<LoggingEmailTransport>>())
        : new SmtpEmailTransport(emailOptions));
builder.Services.AddSingleton<EmailSender>();

// Надёжная очередь писем: принятая доставка живёт в PostgreSQL, содержимое
// зашифровано ключом из конфигурации, отправка — фоновая с ограниченными повторами.
var outboxOptions = EmailOutboxOptions.Read(builder.Configuration);
builder.Services.AddSingleton(outboxOptions);
builder.Services.AddSingleton<IOutboxPayloadProtector>(
    new AesGcmOutboxPayloadProtector(
        OutboxProtectionKey.Derive(builder.Configuration, jwtOptions.Secret)));
builder.Services.AddScoped<EmailOutbox>();
builder.Services.AddScoped<EmailOutboxProcessor>();
builder.Services.AddSingleton<EmailDispatchTrigger>();
builder.Services.AddHostedService<EmailDeliveryWorker>();

var authCodeOptions = ReadAuthCodeOptions(builder.Configuration);
builder.Services.AddSingleton(authCodeOptions);
var authRateLimitOptions = AuthRateLimitOptions.Read(builder.Configuration);
builder.Services.AddSingleton(authRateLimitOptions);
builder.Services.AddSingleton(services =>
    new FixedWindowRateLimiter(services.GetRequiredService<AuthRateLimitOptions>().MaxTrackedKeys));
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<IAuthCodeGenerator, RandomAuthCodeGenerator>();
builder.Services.AddScoped<AuthCodeLifecycle>();
builder.Services.AddScoped<EmailVerificationService>();
builder.Services.AddScoped<PasswordResetService>();
builder.Services.AddScoped<AuthSessionValidator>();
builder.Services.AddScoped<CurrentUserContext>();
builder.Services.AddScoped<FamilyService>();
builder.Services.AddScoped<SourceFamilyNameResolver>();
builder.Services.AddScoped<ExternalRecipeSourceLoader>();
builder.Services.AddScoped<ExternalRecipeStateResolver>();
builder.Services.AddScoped<ExternalRecipePromotionService>();
builder.Services.AddScoped<RecipeMutationService>();
builder.Services.AddScoped<RecipeSharingService>();
builder.Services.AddScoped<RepetitionCounter>();
builder.Services.AddScoped<WeekPlanSaver>();
builder.Services.AddScoped<AdminBootstrap>();

builder.Services.AddSingleton(services => new ShareOptions
{
    BaseUrl = services.GetRequiredService<IConfiguration>()["SHARE_BASE_URL"] ?? ""
});

var otlpEnabled = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
var otelResource = ResourceBuilder.CreateDefault().AddService("menu-planner-api");

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("menu-planner-api"))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation();
        if (otlpEnabled) tracing.AddOtlpExporter();
    });

builder.Logging.AddOpenTelemetry(options =>
{
    options.SetResourceBuilder(otelResource);
    options.IncludeScopes = true;
    options.IncludeFormattedMessage = true;
    if (otlpEnabled) options.AddOtlpExporter();
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context => context.HttpContext.RequestServices
                .GetRequiredService<AuthSessionValidator>()
                .ValidateAsync(context)
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Закрытая операторская команда назначения первоначального администратора.
// Выполняется вне публичного HTTP-маршрута и завершает процесс до старта
// веб-сервера; применяет миграции и читает пароль скрыто из stdin.
if (AdminBootstrapArgs.IsCommand(args))
{
    Environment.ExitCode = await AdminBootstrapCommand.RunAsync(app.Services, args);
    return;
}

// X-Forwarded-* принимаются только когда задан доверенный прокси. Без него
// middleware не подключается вовсе: пустой список известных прокси в ASP.NET
// Core трактуется как «доверять всем», что позволило бы подделать IP.
var forwardedHeaders = ForwardedHeaderConfiguration.Build(app.Configuration);
if (ForwardedHeaderConfiguration.HasTrustedProxies(forwardedHeaders))
    app.UseForwardedHeaders(forwardedHeaders);

app.Services.GetRequiredService<PhotoStorage>();

app.MapGet("/health", () => Results.Json(
    new { status = "ok", service = "menu-planner-api" }));

app.MapGet("/ready", ReadinessEndpoint.GetReadinessAsync);

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

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsRelational())
        await db.Database.MigrateAsync();
}

app.Run();

static JwtOptions ReadJwtOptions(ConfigurationManager configuration)
{
    var options = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
        ?? new JwtOptions();

    if (configuration["JWT_SECRET"] is { Length: > 0 } secret) options.Secret = secret;
    if (configuration["JWT_ISSUER"] is { Length: > 0 } issuer) options.Issuer = issuer;
    if (configuration["JWT_AUDIENCE"] is { Length: > 0 } audience) options.Audience = audience;
    if (int.TryParse(configuration["JWT_EXPIRY_MINUTES"], out var minutes)) options.ExpiryMinutes = minutes;

    if (options.Secret.Length < 32)
        throw new InvalidOperationException(
            "Jwt:Secret (или env JWT_SECRET) должен быть не короче 32 символов.");

    return options;
}

static AuthCodeOptions ReadAuthCodeOptions(ConfigurationManager configuration)
{
    var options = configuration.GetSection(AuthCodeOptions.SectionName).Get<AuthCodeOptions>()
        ?? new AuthCodeOptions();

    if (int.TryParse(configuration["AUTH_CODE_MAX_ATTEMPTS"], out var maxAttempts))
        options.MaxAttempts = maxAttempts;
    if (int.TryParse(configuration["AUTH_CODE_LOCK_DAYS"], out var lockDays))
        options.LockDurationDays = lockDays;
    if (int.TryParse(configuration["AUTH_CODE_RESEND_COOLDOWN_MINUTES"], out var cooldownMinutes))
        options.ResendCooldownMinutes = cooldownMinutes;
    if (int.TryParse(configuration["AUTH_CODE_RESEND_RATE_LIMIT_PER_HOUR"], out var rateLimit))
        options.ResendRateLimitPerHour = rateLimit;

    options.Validate();
    return options;
}

public partial class Program { }
