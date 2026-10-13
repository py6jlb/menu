using Microsoft.AspNetCore.Identity;
using MenuPlanner.Api.Features.Auth.Accounts;
using MenuPlanner.Api.Features.Auth.Admin;
using MenuPlanner.Api.Features.Auth.Codes;
using MenuPlanner.Api.Features.Auth.PasswordReset;
using MenuPlanner.Api.Features.Auth.RateLimiting;
using MenuPlanner.Api.Features.Auth.Sessions;
using MenuPlanner.Api.Features.Auth.Verification;
using MenuPlanner.Domain.Accounts;

namespace MenuPlanner.Api.Features.Auth;

/// <summary>
/// Регистрация аккаунтов и кодов: хэширование пароля, коды подтверждения/сброса,
/// ограничение частоты, сессии и операторский bootstrap администратора.
/// </summary>
public static class AuthServiceCollectionExtensions
{
    public static IServiceCollection AddAuth(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

        var authCodeOptions = AuthCodeOptions.Read(configuration);
        services.AddSingleton(authCodeOptions);
        var authRateLimitOptions = AuthRateLimitOptions.Read(configuration);
        services.AddSingleton(authRateLimitOptions);
        services.AddSingleton(provider =>
            new FixedWindowRateLimiter(provider.GetRequiredService<AuthRateLimitOptions>().MaxTrackedKeys));
        services.AddSingleton<IAuthCodeGenerator, RandomAuthCodeGenerator>();
        services.AddScoped<AuthCodeLifecycle>();
        services.AddScoped<EmailVerificationService>();
        services.AddScoped<PasswordResetService>();
        services.AddScoped<AuthSessionValidator>();
        services.AddScoped<CurrentUserContext>();
        services.AddScoped<UserAccountStore>();
        services.AddScoped<AdminBootstrap>();
        return services;
    }
}
