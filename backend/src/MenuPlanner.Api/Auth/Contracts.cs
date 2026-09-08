using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Auth;

public sealed record RegisterRequest(string? Email, string? Password);

public sealed record LoginRequest(string? Email, string? Password);

public sealed record VerifyEmailRequest(string? Code);

public sealed record UserDto(Guid Id, string Email, string Role, bool IsEmailVerified)
{
    public static UserDto From(User user) => new(user.Id, user.Email, user.Role.ToString(), user.IsEmailVerified);
}

public sealed record AuthResponse(string Token, UserDto User);

public sealed record ErrorDto(string Error);
