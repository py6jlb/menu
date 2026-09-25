using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Auth.Codes;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Emails;

namespace MenuPlanner.Api.Auth;

public static class AuthEndpoints
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
        EmailSender emailSender)
    {
        var email = request.Email?.Trim().ToLowerInvariant() ?? "";
        if (!EmailRegex.IsMatch(email))
            return Results.BadRequest(new ErrorDto("Некорректный email."));

        var password = request.Password ?? "";
        if (password.Length < PasswordPolicy.MinLength)
            return Results.BadRequest(new ErrorDto(PasswordPolicy.TooShortMessage));

        if (await db.Users.AnyAsync(u => u.Email == email))
            return Results.Conflict(new ErrorDto("Пользователь с таким email уже существует."));

        var isFirstUser = !await db.Users.AnyAsync();
        var user = new User
        {
            Email = email,
            PasswordHash = passwordHasher.HashPassword(null!, password),
            Role = isFirstUser ? UserRole.Admin : UserRole.User,
            CreatedAt = DateTime.UtcNow
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        var code = await AuthCodeIssuer.IssueAsync(
            db, passwordHasher, user.Id, AuthCodeType.Verify, DateTime.UtcNow);
        await emailSender.SendVerificationCodeAsync(email, code);

        return Results.Json(
            new AuthResponse(tokenService.CreateToken(user), UserDto.From(user)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        AppDbContext db,
        IPasswordHasher<User> passwordHasher,
        JwtTokenService tokenService)
    {
        var email = request.Email?.Trim().ToLowerInvariant() ?? "";
        var password = request.Password ?? "";

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
