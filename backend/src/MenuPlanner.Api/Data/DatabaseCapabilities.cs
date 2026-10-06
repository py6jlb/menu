using Microsoft.EntityFrameworkCore;

namespace MenuPlanner.Api.Data;

/// <summary>
/// Возможности провайдера БД, от которых зависит домен. Атомарные блокировки и
/// условные обновления строк (FOR UPDATE, advisory-локи, ExecuteUpdate) есть
/// только у реляционного провайдера; на InMemory в быстрых тестах эти шаги
/// пропускаются. Единый seam, чтобы проверка провайдера не расползалась по
/// сервисам.
/// </summary>
public static class DatabaseCapabilities
{
    public static bool SupportsRelationalLocking(this AppDbContext db) =>
        db.Database.IsRelational();
}
