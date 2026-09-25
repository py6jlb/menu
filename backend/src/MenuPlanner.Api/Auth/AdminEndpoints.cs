using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Auth.Codes;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Auth;

public static class AdminEndpoints
{
    public const int UnlockRateLimitPerMinute = 10;
    private static readonly TimeSpan UnlockRateWindow = TimeSpan.FromMinutes(1);
    private const string UnlockRateKeyPrefix = "admin-unlock:user:";

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
        FixedWindowRateLimiter limiter)
    {
        var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var adminId))
            return Results.Unauthorized();

        var now = DateTime.UtcNow;
        if (!limiter.TryConsume(UnlockRateKeyPrefix + adminId, UnlockRateLimitPerMinute, UnlockRateWindow, now))
            return Results.Json(
                new ErrorDto("Слишком много запросов. Попробуйте позже."),
                statusCode: StatusCodes.Status429TooManyRequests);

        var email = request.Email?.Trim().ToLowerInvariant() ?? "";
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null)
            return Results.NotFound(new ErrorDto("Пользователь не найден."));

        AuthCodeService.ResetAttempts(user);
        await db.SaveChangesAsync();

        return Results.Ok(new MessageDto("Пользователь разблокирован."));
    }
}
