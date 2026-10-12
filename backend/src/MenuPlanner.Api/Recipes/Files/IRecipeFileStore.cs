namespace MenuPlanner.Api.Recipes.Files;

/// <summary>Файл, лежащий в хранилище: имя без пути и время последней записи (UTC).</summary>
public sealed record StoredFile(string Name, DateTime LastWriteUtc);

/// <summary>
/// Порт низкоуровневого файлового хранилища файлов рецептов. Предметный модуль
/// <see cref="RecipeFileLifecycle"/> и уборка <see cref="RecipeGarbageCollector"/>
/// работают через него, а тесты подменяют реализацию, не трогая диск. Вид задаёт
/// отдельный каталог, поэтому присутствует в каждом методе.
/// </summary>
public interface IRecipeFileStore
{
    Task<string> SaveAsync(
        RecipeFileKind kind,
        Guid recipeId,
        string extension,
        Stream stream,
        CancellationToken cancellationToken = default);

    Task<string?> CopyAsync(
        RecipeFileKind kind,
        Guid recipeId,
        string? sourceStoredName,
        CancellationToken cancellationToken = default);

    void Delete(RecipeFileKind kind, string? storedName);

    string? ResolveReadPath(RecipeFileKind kind, string? fileName);

    /// <summary>Все файлы каталога вида с временем записи; политику отбора задаёт вызывающий.</summary>
    IReadOnlyList<StoredFile> List(RecipeFileKind kind);
}
