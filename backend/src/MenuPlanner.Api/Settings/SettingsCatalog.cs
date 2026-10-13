namespace MenuPlanner.Api.Settings;

/// <summary>Границы окна повторяемости рецептов и значение по умолчанию.</summary>
public static class SettingsCatalog
{
    public const int DefaultRepetitionWindowWeeks = 3;
    public const int MinRepetitionWindowWeeks = 1;
    public const int MaxRepetitionWindowWeeks = 52;
}