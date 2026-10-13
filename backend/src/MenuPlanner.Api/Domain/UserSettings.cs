namespace MenuPlanner.Api.Domain;

/// <summary>Пользовательские настройки: окно повторяемости рецептов в неделях.</summary>
public sealed class UserSettings
{
    public Guid UserId { get; set; }
    public int RepetitionWindowWeeks { get; set; }
}