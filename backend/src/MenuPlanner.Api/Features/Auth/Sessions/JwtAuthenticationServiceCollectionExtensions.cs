using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MenuPlanner.Infrastructure.Configuration;

namespace MenuPlanner.Api.Features.Auth.Sessions;

/// <summary>
/// Регистрация аутентификации по JWT: опции, сервис выпуска токена и middleware
/// проверки токена с валидацией версии сессии через <see cref="AuthSessionValidator"/>.
/// </summary>
public static class JwtAuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        var jwtOptions = JwtOptions.Read(configuration);
        services.AddSingleton(jwtOptions);
        services.AddSingleton<JwtTokenService>();

        services
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
        services.AddAuthorization();
        return services;
    }
}
