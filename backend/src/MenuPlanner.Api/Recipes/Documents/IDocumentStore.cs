namespace MenuPlanner.Api.Recipes.Documents;

/// <summary>Файл, лежащий в хранилище документов: имя без пути и время последней записи (UTC).</summary>
public sealed record StoredDocument(string Name, DateTime LastWriteUtc);

/// <summary>
/// Порт низкоуровневого файлового хранилища PDF-документов рецептов. Предметный
/// модуль <see cref="DocumentLifecycle"/> и уборка <see cref="DocumentGarbageCollector"/>
/// работают через него, а тесты подменяют реализацию, не трогая диск.
/// </summary>
public interface IDocumentStore
{
    Task<string> SaveAsync(
        Guid recipeId, string extension, Stream stream, CancellationToken cancellationToken = default);

    Task<string?> CopyAsync(
        Guid recipeId, string? sourceStoredName, CancellationToken cancellationToken = default);

    void Delete(string? storedName);

    string? ResolveReadPath(string? fileName);

    /// <summary>Все файлы каталога с временем записи; политику отбора задаёт вызывающий.</summary>
    IReadOnlyList<StoredDocument> List();
}
