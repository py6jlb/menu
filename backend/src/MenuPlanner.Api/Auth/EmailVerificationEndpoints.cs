using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Auth.Codes;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Emails;

namespace MenuPlanner.Api.Auth;

public static class EmailVerificationEndpoints
{
    private const string EmailKeyPrefix = "verify-resend:email:";
    private const string IpKeyPrefix = "verify-resend:ip:";

    public static IEndpointRouteBuilder MapEmailVerificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/verify", VerifyEmailAsync).RequireAuthorization();
        group.MapPost("/verify/resend", ResendVerificationAsync).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> ResendVerificationAsync(
        ClaimsPrincipal principal,
        AppDbContext db,
        IPasswordHasher<User> hasher,
        EmailSender emailSender,
        AuthCodeOptions options,
        FixedWindowRateLimiter limiter,
        HttpContext http)
    {
        var user = await CurrentUserAsync(principal, db);
        if (user is null)
            return Results.Unauthorized();

        var now = DateTime.UtcNow;
        if (user.IsEmailVerified)
            return Results.Conflict(new ErrorDto("Почта уже подтверждена."));

        var window = TimeSpan.FromHours(1);
        var ip = ClientIp(http);
        var allowedByEmail = limiter.TryConsume(EmailKeyPrefix + user.Email, options.ResendRateLimitPerHour, window, now);
        var allowedByIp = limiter.TryConsume(IpKeyPrefix + ip, options.ResendRateLimitPerHour, window, now);
        if (!allowedByEmail || !allowedByIp)
            return Results.Json(
                new ErrorDto("Слишком много запросов. Попробуйте позже."),
                statusCode: StatusCodes.Status429TooManyRequests);

        AuthCodeService.ClearExpiredLock(user, now);
        if (AuthCodeService.IsLocked(user, now))
            return Locked();

        var latest = await LatestVerifyCodeAsync(db, user.Id);

        var cooldown = TimeSpan.FromMinutes(options.ResendCooldownMinutes);
        if (latest is not null && now - latest.CreatedAt < cooldown)
        {
            var left = (int)Math.Ceiling((cooldown - (now - latest.CreatedAt)).TotalSeconds);
            return Results.Json(
                new ErrorDto($"Повторная отправка будет доступна через {left} сек."),
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        var code = await EmailVerificationService.IssueCodeAsync(db, hasher, user.Id, now);
        await emailSender.SendVerificationCodeAsync(user.Email, code);
        return Results.Ok();
    }

    private static string ClientIp(HttpContext http)
    {
        var forwarded = http.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(first))
                return first;
        }

        return http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static async Task<IResult> VerifyEmailAsync(
        VerifyEmailRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        IPasswordHasher<User> hasher,
        AuthCodeOptions options)
    {
        var user = await CurrentUserAsync(principal, db);
        if (user is null)
            return Results.Unauthorized();

        var now = DateTime.UtcNow;
        if (user.IsEmailVerified)
            return Results.Conflict(new ErrorDto("Почта уже подтверждена."));

        AuthCodeService.ClearExpiredLock(user, now);
        if (AuthCodeService.IsLocked(user, now))
            return Locked();

        var stored = await LatestVerifyCodeAsync(db, user.Id);

        var code = request.Code?.Trim() ?? "";
        var result = stored is null
            ? CodeCheckResult.Invalid
            : AuthCodeService.Check(hasher, stored, code, now);

        if (result == CodeCheckResult.Ok)
        {
            AuthCodeService.Burn(stored!);
            user.IsEmailVerified = true;
            user.EmailVerifiedAt = now;
            AuthCodeService.ResetAttempts(user);
            await db.SaveChangesAsync();
            return Results.Json(UserDto.From(user));
        }

        AuthCodeService.RecordFailedAttempt(
            user, now, options.MaxAttempts, TimeSpan.FromDays(options.LockDurationDays));
        await db.SaveChangesAsync();

        return AuthCodeService.IsLocked(user, now)
            ? Locked()
            : Results.BadRequest(new ErrorDto("Неверный или истёкший код."));
    }

    private static IResult Locked() =>
        Results.Json(
            new ErrorDto("Слишком много неверных попыток. Попробуйте позже."),
            statusCode: StatusCodes.Status423Locked);

    private static Task<AuthCode?> LatestVerifyCodeAsync(AppDbContext db, Guid userId) =>
        db.AuthCodes
            .Where(c => c.UserId == userId && c.Type == AuthCodeType.Verify)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync();

    private static async Task<User?> CurrentUserAsync(ClaimsPrincipal principal, AppDbContext db)
    {
        var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var userId))
            return null;

        return await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
    }
}
