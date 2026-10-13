namespace MenuPlanner.Api.Settings;

/// <summary>Тело запроса обновления настроек: окно повторяемости в неделях.</summary>
public sealed record UserSettingsRequest(int? RepetitionWindowWeeks);

/// <summary>Настройки пользователя в ответе.</summary>
public sealed record UserSettingsDto(int RepetitionWindowWeeks);

/// <summary>Простой текстовый ответ об ошибке настроек.</summary>
public sealed record SettingsErrorDto(string Error);