namespace MenuPlanner.Api.Auth;

/// <summary>Правила пароля: минимальная длина и сообщение об ошибке.</summary>
public static class PasswordPolicy
{
    public const int MinLength = 6;

    public static readonly string TooShortMessage =
        $"Пароль должен содержать минимум {MinLength} символов.";
}
