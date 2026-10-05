using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Auth.Codes;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Emails;
using MenuPlanner.Api.Families;
using MenuPlanner.Api.Health;
using MenuPlanner.Api.Ingredients;
using MenuPlanner.Api.Plans;
using MenuPlanner.Api.Recipes;
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
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<JwtTokenService>();

var emailOptions = ReadEmailOptions(builder.Configuration);
builder.Services.AddSingleton(emailOptions);
builder.Services.AddSingleton<IEmailTransport>(services =>
    string.IsNullOrWhiteSpace(emailOptions.Host)
        ? new LoggingEmailTransport(services.GetRequiredService<ILogger<LoggingEmailTransport>>())
        : new SmtpEmailTransport(emailOptions));
builder.Services.AddSingleton<EmailSender>();

var authCodeOptions = ReadAuthCodeOptions(builder.Configuration);
builder.Services.AddSingleton(authCodeOptions);
builder.Services.AddSingleton<FixedWindowRateLimiter>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<IAuthCodeGenerator, RandomAuthCodeGenerator>();
builder.Services.AddScoped<EmailVerificationService>();

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
            OnTokenValidated = TokenVersionValidator.ValidateAsync
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeaders.KnownIPNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
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

static EmailOptions ReadEmailOptions(ConfigurationManager configuration)
{
    var options = new EmailOptions();

    if (configuration["SMTP_HOST"] is { Length: > 0 } host) options.Host = host;
    if (configuration["SMTP_PORT"] is { Length: > 0 } port && int.TryParse(port, out var portValue))
        options.Port = portValue;
    if (configuration["SMTP_USER"] is { Length: > 0 } user) options.User = user;
    if (configuration["SMTP_PASSWORD"] is { Length: > 0 } password) options.Password = password;
    if (configuration["SMTP_FROM"] is { Length: > 0 } from) options.From = from;
    if (configuration["SMTP_FROM_NAME"] is { Length: > 0 } fromName) options.FromName = fromName;
    if (configuration["SMTP_ENABLE_STARTTLS"] is { Length: > 0 } startTls &&
        bool.TryParse(startTls, out var enableStartTls))
        options.EnableStartTls = enableStartTls;

    if (!string.IsNullOrWhiteSpace(options.Host) && string.IsNullOrWhiteSpace(options.From))
        throw new InvalidOperationException(
            "SMTP_FROM обязателен, когда задан SMTP_HOST.");

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

    return options;
}

public partial class Program { }
