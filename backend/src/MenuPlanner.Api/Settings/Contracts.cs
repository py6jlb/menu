namespace MenuPlanner.Api.Settings;

public sealed record UserSettingsRequest(int? RepetitionWindowWeeks);

public sealed record UserSettingsDto(int RepetitionWindowWeeks);

public sealed record SettingsErrorDto(string Error);