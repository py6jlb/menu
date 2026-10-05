using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Auth;

public sealed record RegisterRequest(string? Email, string? Password);

public sealed record LoginRequest(string? Email, string? Password);

public sealed record VerifyEmailRequest(string? Code);

public sealed record UnlockUserRequest(string? Email);

public sealed record PasswordResetCodeRequest(string? Email);

public sealed record ResetPasswordRequest(
    string? Email,
    string? Code,
    string? NewPassword,
    string? NewPasswordConfirm);

public sealed record MessageDto(string Message);

public sealed record UserDto(Guid Id, string Email, string Role, bool IsEmailVerified)
{
    public static UserDto From(User user) => new(user.Id, user.Email, user.Role.ToString(), user.IsEmailVerified);
}

public sealed record AuthResponse(string Token, UserDto User);

public sealed record ErrorDto(string Error);

/// <summary>Ошибка ввода кода подтверждения: машиночитаемый код плюс русский текст.</summary>
public sealed record VerifyErrorDto(string Error, string Code);

/// <summary>Ошибка ввода кода сброса: машиночитаемый код плюс русский текст.</summary>
public sealed record ResetErrorDto(string Error, string Code);
