using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Emails;

namespace MenuPlanner.Api.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/register", RegisterAsync);
        group.MapPost("/login", LoginAsync);
        group.MapGet("/me", MeAsync).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        AppDbContext db,
        IPasswordHasher<User> passwordHasher,
        JwtTokenService tokenService,
        EmailSender emailSender,
        EmailVerificationService verification,
        ILogger<EmailSender> logger,
        AuthRateLimitOptions rateLimits,
        FixedWindowRateLimiter limiter,
        TimeProvider clock,
        HttpContext http)
    {
        var email = EmailPolicy.Normalize(request.Email);
        if (!EmailPolicy.IsValid(email))
            return Results.BadRequest(new ErrorDto("Некорректный email."));

        var limited = AuthRateLimitPolicy.Check(
            limiter,
            scope: "register",
            ip: ClientIpResolver.Resolve(http),
            perIp: rateLimits.RegisterPerIpPerHour,
            perIdentity: rateLimits.RegisterPerEmailPerHour,
            identity: email,
            window: TimeSpan.FromHours(1),
            now: clock.GetUtcNow().UtcDateTime);
        if (limited is not null)
            return limited;

        var password = request.Password ?? "";
        if (password.Length < PasswordPolicy.MinLength)
            return Results.BadRequest(new ErrorDto(PasswordPolicy.TooShortMessage));

        if (await db.Users.AnyAsync(u => u.Email == email))
            return Results.Conflict(new ErrorDto("Пользователь с таким email уже существует."));

        // Публичная регистрация всегда выдаёт только роль Пользователя.
        // Системная роль Администратора назначается отдельной закрытой
        // процедурой (AdminBootstrap), а не первым обратившимся.
        var user = new User
        {
            Email = email,
            PasswordHash = passwordHasher.HashPassword(null!, password),
            Role = UserRole.User,
            CreatedAt = clock.GetUtcNow().UtcDateTime
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        var code = await verification.IssueInitialCodeAsync(user);
        try
        {
            await emailSender.SendVerificationCodeAsync(email, code, http.RequestAborted);
        }
        catch (EmailDeliveryException failure)
        {
            EmailDeliveryFailure.LogSafe(logger, failure);
            return Results.Json(
                new ErrorDto(EmailDeliveryFailure.UserMessage),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Json(
            new AuthResponse(tokenService.CreateToken(user), UserDto.From(user)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        AppDbContext db,
        IPasswordHasher<User> passwordHasher,
        JwtTokenService tokenService,
        AuthRateLimitOptions rateLimits,
        FixedWindowRateLimiter limiter,
        TimeProvider clock,
        HttpContext http)
    {
        var email = request.Email?.Trim().ToLowerInvariant() ?? "";
        var password = request.Password ?? "";

        var limited = AuthRateLimitPolicy.Check(
            limiter,
            scope: "login",
            ip: ClientIpResolver.Resolve(http),
            perIp: rateLimits.LoginPerIpPerMinute,
            perIdentity: rateLimits.LoginPerEmailPerMinute,
            identity: email,
            window: TimeSpan.FromMinutes(1),
            now: clock.GetUtcNow().UtcDateTime);
        if (limited is not null)
            return limited;

        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);
        if (user is null)
            return Unauthorized();

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
            return Unauthorized();

        return Results.Json(new AuthResponse(tokenService.CreateToken(user), UserDto.From(user)));
    }

    private static async Task<IResult> MeAsync(ClaimsPrincipal principal, AppDbContext db)
    {
        var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var userId))
            return Results.Unauthorized();

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
            return Results.Unauthorized();

        return Results.Json(UserDto.From(user));
    }

    private static IResult Unauthorized() =>
        Results.Json(new ErrorDto("Неверный email или пароль."), statusCode: StatusCodes.Status401Unauthorized);
}
