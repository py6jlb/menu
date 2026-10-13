using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Emails.Outbox;

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
        UserAccountStore users,
        IPasswordHasher<User> passwordHasher,
        JwtTokenService tokenService,
        EmailVerificationService verification,
        EmailDispatchTrigger dispatch,
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

        if (await users.EmailExistsAsync(email))
            return Results.Conflict(new ErrorDto("Пользователь с таким email уже существует."));

        // Публичная регистрация всегда выдаёт только роль Пользователя.
        // Системная роль Администратора назначается отдельной закрытой
        // процедурой (AdminBootstrap), а не первым обратившимся.
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = passwordHasher.HashPassword(null!, password),
            Role = UserRole.User,
            CreatedAt = clock.GetUtcNow().UtcDateTime
        };

        // Аккаунт, challenge и принятая к отправке доставка фиксируются одной
        // транзакцией. Временная недоступность SMTP не мешает регистрации:
        // сессия выдаётся сразу, письмо доводит фоновая очередь.
        users.Add(user);
        await verification.IssueInitialCodeAsync(user, http.RequestAborted);
        await dispatch.TryDispatchAsync(http.RequestAborted);

        return Results.Json(
            new AuthResponse(tokenService.CreateToken(user), UserDto.From(user)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        UserAccountStore users,
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

        var user = await users.FindByEmailAsync(email);
        if (user is null)
            return Unauthorized();

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
            return Unauthorized();

        return Results.Json(new AuthResponse(tokenService.CreateToken(user), UserDto.From(user)));
    }

    private static async Task<IResult> MeAsync(ClaimsPrincipal principal, CurrentUserContext currentUser)
    {
        var user = await currentUser.UserAsync(principal);
        if (user is null)
            return Results.Unauthorized();

        return Results.Json(UserDto.From(user));
    }

    private static IResult Unauthorized() =>
        Results.Json(new ErrorDto("Неверный email или пароль."), statusCode: StatusCodes.Status401Unauthorized);
}
