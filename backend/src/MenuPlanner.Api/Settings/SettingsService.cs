using Microsoft.EntityFrameworkCore;
using Npgsql;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Settings;

/// <summary>
/// Чтение и запись личных настроек поверх БД. Чтение отсутствующей строки не
/// создаёт запись: оно возвращает значение по умолчанию, поэтому повторные
/// GET не пишут и не конфликтуют между читателями. Запись создаёт строку при
/// первой явной правке, а гонку первой вставки разрешает на уровне уникального
/// ключа пользователя: проигравший запрос применяет значение к уже созданной
/// строке вместо необработанного конфликта.
/// </summary>
public sealed class SettingsService
{
    private readonly AppDbContext _db;

    public SettingsService(AppDbContext db) => _db = db;

    /// <summary>
    /// Настройки пользователя; при отсутствии строки — значения по умолчанию
    /// без записи в БД.
    /// </summary>
    public async Task<UserSettingsDto> ReadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var settings = await _db.UserSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

        return new UserSettingsDto(settings?.RepetitionWindowWeeks ?? SettingsCatalog.DefaultRepetitionWindowWeeks);
    }

    /// <summary>
    /// Явное сохранение окна повторяемости. Создаёт строку, если её ещё нет, и
    /// обновляет существующую. Конкурентная первая вставка не приводит к ошибке:
    /// после уникального конфликта значение применяется к созданной строке.
    /// </summary>
    public async Task<UserSettingsDto> SaveAsync(
        Guid userId,
        int repetitionWindowWeeks,
        CancellationToken cancellationToken = default)
    {
        var settings = await _db.UserSettings
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

        if (settings is null)
        {
            settings = new UserSettings { UserId = userId, RepetitionWindowWeeks = repetitionWindowWeeks };
            _db.UserSettings.Add(settings);
        }
        else
        {
            settings.RepetitionWindowWeeks = repetitionWindowWeeks;
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Строку успел создать параллельный запрос: попытка вставки откачена,
            // очищаем трекер и применяем значение к существующей строке. Ошибки, не
            // связанные с гонкой вставки, не подавляются этим catch.
            _db.ChangeTracker.Clear();
            settings = await _db.UserSettings
                .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
            if (settings is null)
                throw;

            settings.RepetitionWindowWeeks = repetitionWindowWeeks;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new UserSettingsDto(settings.RepetitionWindowWeeks);
    }
}
