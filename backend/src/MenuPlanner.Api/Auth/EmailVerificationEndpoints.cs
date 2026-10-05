using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Emails;

namespace MenuPlanner.Api.Auth;

public static class EmailVerificationEndpoints
{
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
        EmailVerificationService verification,
        EmailSender emailSender,
        ILogger<EmailSender> logger,
        AuthCodeOptions options,
        FixedWindowRateLimiter limiter,
        TimeProvider clock,
        HttpContext http)
    {
        var user = await CurrentUserAsync(principal, db);
        if (user is null)
            return Results.Unauthorized();

        var now = clock.GetUtcNow().UtcDateTime;
        if (user.IsEmailVerified)
            return Results.Conflict(new ErrorDto("Почта уже подтверждена."));

        var limited = AuthRateLimitPolicy.Check(
            limiter,
            scope: "verify-resend",
            ip: ClientIpResolver.Resolve(http),
            perIp: options.ResendRateLimitPerHour,
            perIdentity: options.ResendRateLimitPerHour,
            identity: user.Email,
            window: TimeSpan.FromHours(1),
            now: now);
        if (limited is not null)
            return limited;

        var result = await verification.ResendAsync(user);
        return result.Outcome switch
        {
            ResendEmailOutcome.Sent => await SendAsync(
                emailSender, logger, user.Email, result.Code!, http.RequestAborted),
            ResendEmailOutcome.AlreadyVerified => Results.Conflict(new ErrorDto("Почта уже подтверждена.")),
            ResendEmailOutcome.TooSoon => RateLimitResults.TooManyRequests(
                result.RetryAfterSeconds,
                $"Повторная отправка будет доступна через {result.RetryAfterSeconds} сек."),
            _ => Locked()
        };
    }

    private static async Task<IResult> VerifyEmailAsync(
        VerifyEmailRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        EmailVerificationService verification)
    {
        var user = await CurrentUserAsync(principal, db);
        if (user is null)
            return Results.Unauthorized();

        var result = await verification.VerifyAsync(user, request.Code?.Trim() ?? "");
        return result.Outcome switch
        {
            VerifyEmailOutcome.Verified => Results.Json(UserDto.From(result.User!)),
            VerifyEmailOutcome.AlreadyVerified => Results.Conflict(new ErrorDto("Почта уже подтверждена.")),
            VerifyEmailOutcome.Locked => Locked(),
            VerifyEmailOutcome.ExpiredCode => BadCode(
                "Срок действия кода истёк. Запросите новый код.", "expired"),
            VerifyEmailOutcome.CodeAlreadyUsed => BadCode(
                "Этот код уже использован. Запросите новый код.", "used"),
            _ => BadCode("Неверный код. Проверьте и попробуйте снова.", "invalid")
        };
    }

    private static async Task<IResult> SendAsync(
        EmailSender emailSender,
        ILogger<EmailSender> logger,
        string email,
        string code,
        CancellationToken cancellationToken)
    {
        try
        {
            await emailSender.SendVerificationCodeAsync(email, code, cancellationToken);
        }
        catch (EmailDeliveryException failure)
        {
            EmailDeliveryFailure.LogSafe(logger, failure);
            return Results.Json(
                new ErrorDto(EmailDeliveryFailure.UserMessage),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok();
    }

    private static IResult BadCode(string message, string code) =>
        Results.BadRequest(new VerifyErrorDto(message, code));

    private static IResult Locked() =>
        Results.Json(
            new ErrorDto("Слишком много неверных попыток. Попробуйте позже."),
            statusCode: StatusCodes.Status423Locked);

    private static async Task<User?> CurrentUserAsync(ClaimsPrincipal principal, AppDbContext db)
    {
        var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var userId))
            return null;

        return await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
    }
}
