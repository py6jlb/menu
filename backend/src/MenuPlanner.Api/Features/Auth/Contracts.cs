using MenuPlanner.Domain.Accounts;

namespace MenuPlanner.Api.Features.Auth;

/// <summary>Тело запроса регистрации: email и пароль.</summary>
public sealed record RegisterRequest(string? Email, string? Password);

/// <summary>Тело запроса входа: email и пароль.</summary>
public sealed record LoginRequest(string? Email, string? Password);

/// <summary>Тело запроса подтверждения почты: код из письма.</summary>
public sealed record VerifyEmailRequest(string? Code);

/// <summary>Тело запроса административной разблокировки: email пользователя.</summary>
public sealed record UnlockUserRequest(string? Email);

/// <summary>Тело запроса кода сброса пароля: email.</summary>
public sealed record PasswordResetCodeRequest(string? Email);

/// <summary>Тело запроса смены пароля по коду: email, код, новый пароль и его подтверждение.</summary>
public sealed record ResetPasswordRequest(
    string? Email,
    string? Code,
    string? NewPassword,
    string? NewPasswordConfirm);

/// <summary>Простой текстовый ответ.</summary>
public sealed record MessageDto(string Message);

/// <summary>Представление пользователя в API.</summary>
public sealed record UserDto(Guid Id, string Email, string Role, bool IsEmailVerified)
{
    public static UserDto From(User user) => new(user.Id, user.Email, user.Role.ToString(), user.IsEmailVerified);
}

/// <summary>Ответ аутентификации: токен сессии и пользователь.</summary>
public sealed record AuthResponse(string Token, UserDto User);

/// <summary>Простой текстовый ответ об ошибке.</summary>
public sealed record ErrorDto(string Error);

/// <summary>Ошибка ввода кода подтверждения: машиночитаемый код плюс русский текст.</summary>
public sealed record VerifyErrorDto(string Error, string Code);

/// <summary>Ошибка ввода кода сброса: машиночитаемый код плюс русский текст.</summary>
public sealed record ResetErrorDto(string Error, string Code);
