using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Emails;

namespace MenuPlanner.Api.Auth;

public static class PasswordResetEndpoints
{
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
        PasswordResetService reset,
        EmailSender emailSender,
        AuthCodeOptions options,
        FixedWindowRateLimiter limiter,
        TimeProvider clock,
        HttpContext http)
    {
        var email = request.Email?.Trim().ToLowerInvariant() ?? "";
        var limited = AuthRateLimitPolicy.Check(
            limiter,
            scope: "password-reset",
            ip: ClientIpResolver.Resolve(http),
            perIp: options.ResendRateLimitPerHour,
            perIdentity: options.ResendRateLimitPerHour,
            identity: email,
            window: TimeSpan.FromHours(1),
            now: clock.GetUtcNow().UtcDateTime);
        if (limited is not null)
            return limited;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is not null && user.IsEmailVerified)
        {
            var result = await reset.RequestAsync(user);
            if (result.Outcome == PasswordResetRequestOutcome.Sent)
                await emailSender.SendPasswordResetCodeAsync(user.Email, result.Code!);
        }

        // Нейтральный ответ для существующих/неизвестных/неподтверждённых email.
        return Results.Json(new MessageDto(NeutralMessage));
    }

    private static async Task<IResult> ResetPasswordAsync(
        ResetPasswordRequest request,
        AppDbContext db,
        PasswordResetService reset,
        AuthRateLimitOptions rateLimits,
        FixedWindowRateLimiter limiter,
        TimeProvider clock,
        HttpContext http)
    {
        var email = request.Email?.Trim().ToLowerInvariant() ?? "";
        var limited = AuthRateLimitPolicy.Check(
            limiter,
            scope: "password-reset-submit",
            ip: ClientIpResolver.Resolve(http),
            perIp: rateLimits.ResetPerIpPerHour,
            perIdentity: rateLimits.ResetPerEmailPerHour,
            identity: email,
            window: TimeSpan.FromHours(1),
            now: clock.GetUtcNow().UtcDateTime);
        if (limited is not null)
            return limited;

        var newPassword = request.NewPassword ?? "";
        if (newPassword.Length < PasswordPolicy.MinLength)
            return Results.BadRequest(new ErrorDto(PasswordPolicy.TooShortMessage));
        if (!string.Equals(newPassword, request.NewPasswordConfirm, StringComparison.Ordinal))
            return Results.BadRequest(new ErrorDto("Пароли не совпадают."));

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null || !user.IsEmailVerified)
            return InvalidCode();

        var result = await reset.ResetAsync(user, request.Code?.Trim() ?? "", newPassword);
        return result.Outcome switch
        {
            PasswordResetOutcome.Reset => Results.Json(new MessageDto("Пароль изменён.")),
            PasswordResetOutcome.ExpiredCode => BadCode(
                "Срок действия кода истёк. Запросите новый код.", "expired"),
            PasswordResetOutcome.CodeAlreadyUsed => BadCode(
                "Этот код уже использован. Запросите новый код.", "used"),
            PasswordResetOutcome.ChallengeClosed => BadCode(
                "Слишком много неверных попыток. Запросите новый код.", "closed"),
            _ => BadCode("Неверный код. Проверьте и попробуйте снова.", "invalid")
        };
    }

    private static IResult InvalidCode() =>
        Results.BadRequest(new ResetErrorDto("Неверный или истёкший код.", "invalid"));

    private static IResult BadCode(string message, string code) =>
        Results.BadRequest(new ResetErrorDto(message, code));
}
