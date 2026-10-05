using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MenuPlanner.Api.Data;
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
        PasswordResetService reset,
        EmailSender emailSender,
        ILogger<EmailSender> logger,
        AuthCodeOptions options,
        FixedWindowRateLimiter limiter,
        TimeProvider clock,
        HttpContext http)
    {
        var email = request.Email?.Trim().ToLowerInvariant() ?? "";
        var now = clock.GetUtcNow().UtcDateTime;
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
            var result = await reset.RequestAsync(user);
            if (result.Outcome == PasswordResetRequestOutcome.Sent)
            {
                try
                {
                    await emailSender.SendPasswordResetCodeAsync(
                        user.Email, result.Code!, http.RequestAborted);
                }
                catch (EmailDeliveryException failure)
                {
                    // Ответ остаётся нейтральным, иначе сбой отправки раскрыл бы
                    // существование и подтверждённость аккаунта.
                    EmailDeliveryFailure.LogSafe(logger, failure);
                }
            }
        }

        // Нейтральный ответ для существующих/неизвестных/неподтверждённых email.
        return Results.Json(new MessageDto(NeutralMessage));
    }

    private static async Task<IResult> ResetPasswordAsync(
        ResetPasswordRequest request,
        AppDbContext db,
        PasswordResetService reset)
    {
        var newPassword = request.NewPassword ?? "";
        if (newPassword.Length < PasswordPolicy.MinLength)
            return Results.BadRequest(new ErrorDto(PasswordPolicy.TooShortMessage));
        if (!string.Equals(newPassword, request.NewPasswordConfirm, StringComparison.Ordinal))
            return Results.BadRequest(new ErrorDto("Пароли не совпадают."));

        var email = request.Email?.Trim().ToLowerInvariant() ?? "";
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
