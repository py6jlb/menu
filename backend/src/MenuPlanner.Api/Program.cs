using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Families;
using MenuPlanner.Api.Ingredients;
 using MenuPlanner.Api.Plans;
 using MenuPlanner.Api.Recipes;
using MenuPlanner.Api.Settings;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? builder.Configuration["DB_CONNECTION_STRING"]
    ?? "Host=localhost;Port=5432;Database=menu_planner;Username=menu;Password=menu";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

var jwtOptions = ReadJwtOptions(builder.Configuration);
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<JwtTokenService>();

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
    });
builder.Services.AddAuthorization();

var app = builder.Build();

app.MapGet("/health", () => Results.Json(
    new { status = "ok", service = "menu-planner-api" }));

app.MapAuthEndpoints();
app.MapFamilyEndpoints();
app.MapRecipeEndpoints();
app.MapIngredientEndpoints();
app.MapPlanEndpoints();
app.MapSettingsEndpoints();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
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

public partial class Program { }
