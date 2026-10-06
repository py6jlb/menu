namespace MenuPlanner.Api.Recipes.Photos;

/// <summary>Файл, лежащий в хранилище фото: имя без пути и время последней записи (UTC).</summary>
public sealed record StoredPhoto(string Name, DateTime LastWriteUtc);

/// <summary>
/// Порт низкоуровневого файлового хранилища фото рецептов. Предметный модуль
/// <see cref="PhotoLifecycle"/> и уборка <see cref="PhotoGarbageCollector"/>
/// работают через него, а тесты подменяют реализацию, не трогая диск.
/// </summary>
public interface IPhotoStore
{
    Task<string> SaveAsync(
        Guid recipeId, string extension, Stream stream, CancellationToken cancellationToken = default);

    Task<string?> CopyAsync(
        Guid recipeId, string? sourceStoredName, CancellationToken cancellationToken = default);

    void Delete(string? storedName);

    string? ResolveReadPath(string? fileName);

    /// <summary>Все файлы каталога с временем записи; политику отбора задаёт вызывающий.</summary>
    IReadOnlyList<StoredPhoto> List();
}
