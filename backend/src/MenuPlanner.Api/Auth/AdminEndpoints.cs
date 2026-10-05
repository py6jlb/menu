using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Auth;

public static class AdminEndpoints
{
    private static readonly TimeSpan UnlockRateWindow = TimeSpan.FromMinutes(1);

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin");

        group.MapPost("/unlock", UnlockAsync)
            .RequireAuthorization(policy => policy.RequireRole(UserRole.Admin.ToString()));

        return app;
    }

    private static async Task<IResult> UnlockAsync(
        UnlockUserRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        AuthRateLimitOptions rateLimits,
        FixedWindowRateLimiter limiter,
        EmailVerificationService verification,
        TimeProvider clock,
        HttpContext http)
    {
        var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var adminId))
            return Results.Unauthorized();

        var limited = AuthRateLimitPolicy.Check(
            limiter,
            scope: "admin-unlock",
            ip: ClientIpResolver.Resolve(http),
            perIp: rateLimits.UnlockPerIpPerMinute,
            perIdentity: rateLimits.UnlockPerAdminPerMinute,
            identity: adminId.ToString(),
            window: UnlockRateWindow,
            now: clock.GetUtcNow().UtcDateTime);
        if (limited is not null)
            return limited;

        var email = request.Email?.Trim().ToLowerInvariant() ?? "";
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null)
            return Results.NotFound(new ErrorDto("Пользователь не найден."));

        await verification.UnlockAsync(user);

        return Results.Ok(new MessageDto("Пользователь разблокирован."));
    }
}
