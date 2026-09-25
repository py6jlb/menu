using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Auth.Codes;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Emails;

namespace MenuPlanner.Api.Auth;

public static class PasswordResetEndpoints
{
    private const string EmailKeyPrefix = "password-reset:email:";
    private const string IpKeyPrefix = "password-reset:ip:";
    private const string NeutralMessage =
        "Если аккаунт существует и почта подтверждена, отправлен код.";

    public static IEndpointRouteBuilder MapPasswordResetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/forgot", RequestPasswordResetAsync);
        group.MapPost("/reset", ResetPasswordAsync);

        return app;
    }

    private static async Task<IResult> RequestPasswordResetAsync(
        PasswordResetCodeRequest request,
        AppDbContext db,
        IPasswordHasher<User> hasher,
        EmailSender emailSender,
        AuthCodeOptions options,
        FixedWindowRateLimiter limiter,
        HttpContext http)
    {
        var email = request.Email?.Trim().ToLowerInvariant() ?? "";
        var now = DateTime.UtcNow;
        var window = TimeSpan.FromHours(1);
        var ip = ClientIpResolver.Resolve(http);
        var allowedByEmail = limiter.TryConsume(
            EmailKeyPrefix + email, options.ResendRateLimitPerHour, window, now);
        var allowedByIp = limiter.TryConsume(
            IpKeyPrefix + ip, options.ResendRateLimitPerHour, window, now);
        if (!allowedByEmail || !allowedByIp)
            return Results.Json(
                new ErrorDto("Слишком много запросов. Попробуйте позже."),
                statusCode: StatusCodes.Status429TooManyRequests);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is not null && user.IsEmailVerified)
        {
            AuthCodeService.ClearExpiredLock(user, now);

            var cooldown = TimeSpan.FromMinutes(options.ResendCooldownMinutes);
            var latest = await LatestResetCodeAsync(db, user.Id);
            var withinCooldown = latest is not null && now - latest.CreatedAt < cooldown;

            if (!AuthCodeService.IsLocked(user, now) && !withinCooldown)
            {
                var code = await AuthCodeIssuer.IssueAsync(db, hasher, user.Id, AuthCodeType.Reset, now);
                await emailSender.SendPasswordResetCodeAsync(user.Email, code);
            }
        }

        return Results.Json(new MessageDto(NeutralMessage));
    }

    private static async Task<IResult> ResetPasswordAsync(
        ResetPasswordRequest request,
        AppDbContext db,
        IPasswordHasher<User> hasher,
        AuthCodeOptions options)
    {
        var newPassword = request.NewPassword ?? "";
        if (newPassword.Length < PasswordPolicy.MinLength)
            return Results.BadRequest(new ErrorDto(PasswordPolicy.TooShortMessage));
        if (!string.Equals(newPassword, request.NewPasswordConfirm, StringComparison.Ordinal))
            return Results.BadRequest(new ErrorDto("Пароли не совпадают."));

        var email = request.Email?.Trim().ToLowerInvariant() ?? "";
        var now = DateTime.UtcNow;
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null || !user.IsEmailVerified)
            return InvalidCode();

        AuthCodeService.ClearExpiredLock(user, now);
        if (AuthCodeService.IsLocked(user, now))
            return Locked();

        var stored = await LatestResetCodeAsync(db, user.Id);
        var code = request.Code?.Trim() ?? "";
        var result = stored is null
            ? CodeCheckResult.Invalid
            : AuthCodeService.Check(hasher, stored, code, now);

        if (result == CodeCheckResult.Ok)
        {
            AuthCodeService.Burn(stored!);
            user.PasswordHash = hasher.HashPassword(user, newPassword);
            user.TokenVersion++;
            AuthCodeService.ResetAttempts(user);
            await db.SaveChangesAsync();
            return Results.Json(new MessageDto("Пароль изменён."));
        }

        AuthCodeService.RecordFailedAttempt(
            user, now, options.MaxAttempts, TimeSpan.FromDays(options.LockDurationDays));
        await db.SaveChangesAsync();

        return AuthCodeService.IsLocked(user, now) ? Locked() : InvalidCode();
    }

    private static IResult InvalidCode() =>
        Results.BadRequest(new ErrorDto("Неверный или истёкший код."));

    private static IResult Locked() =>
        Results.Json(
            new ErrorDto("Слишком много неверных попыток. Попробуйте позже."),
            statusCode: StatusCodes.Status423Locked);

    private static Task<AuthCode?> LatestResetCodeAsync(AppDbContext db, Guid userId) =>
        db.AuthCodes
            .Where(c => c.UserId == userId && c.Type == AuthCodeType.Reset)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync();
}
