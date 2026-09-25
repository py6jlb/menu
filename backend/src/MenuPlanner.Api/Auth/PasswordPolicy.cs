namespace MenuPlanner.Api.Auth;

public static class PasswordPolicy
{
    public const int MinLength = 6;

    public static readonly string TooShortMessage =
        $"Пароль должен содержать минимум {MinLength} символов.";
}
