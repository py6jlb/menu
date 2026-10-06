namespace MenuPlanner.Api.Recipes.Photos;

/// <summary>
/// Файловое хранилище фото на диске сервера. Новая запись защищена от неполного
/// файла: при сбое посреди копирования частичный файл удаляется. Копирование
/// терпимо к исчезнувшему источнику (конкурентная замена фото) и тогда не создаёт
/// файл, а не падает.
/// </summary>
public sealed class PhotoStorage : IPhotoStore
{
    private readonly string _root;

    public PhotoStorage(IConfiguration configuration)
    {
        var dir = configuration["PHOTOS_DIR"];
        _root = Path.GetFullPath(string.IsNullOrWhiteSpace(dir)
            ? Path.Combine(Directory.GetCurrentDirectory(), "photos")
            : dir);
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(
        Guid recipeId, string extension, Stream stream, CancellationToken cancellationToken = default)
    {
        var fileName = $"{recipeId:N}-{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(_root, fileName);
        try
        {
            await using var fileStream = new FileStream(fullPath, FileMode.CreateNew);
            await stream.CopyToAsync(fileStream, cancellationToken);
            return fileName;
        }
        catch
        {
            // Частичная запись не остаётся на диске: уборка увидела бы в ней
            // потенциально актуальный файл.
            TryDeleteFile(fullPath);
            throw;
        }
    }

    public async Task<string?> CopyAsync(
        Guid recipeId, string? sourceStoredName, CancellationToken cancellationToken = default)
    {
        var sourcePath = ResolveReadPath(sourceStoredName);
        if (sourcePath is null) return null;
        var extension = Path.GetExtension(sourcePath);
        try
        {
            await using var stream = File.OpenRead(sourcePath);
            return await SaveAsync(recipeId, extension, stream, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            // Источник заменён/удалён между проверкой и чтением: это гонка, а не
            // ошибка — копии просто нет.
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public void Delete(string? storedName)
    {
        var fullPath = SafePath(storedName);
        if (fullPath is null || !File.Exists(fullPath)) return;
        File.Delete(fullPath);
    }

    public string? ResolveReadPath(string? fileName)
    {
        var fullPath = SafePath(fileName);
        return fullPath is not null && File.Exists(fullPath) ? fullPath : null;
    }

    public IReadOnlyList<StoredPhoto> List()
    {
        if (!Directory.Exists(_root)) return Array.Empty<StoredPhoto>();
        return Directory.EnumerateFiles(_root)
            .Select(path => new StoredPhoto(Path.GetFileName(path), File.GetLastWriteTimeUtc(path)))
            .ToList();
    }

    private static void TryDeleteFile(string fullPath)
    {
        try
        {
            if (File.Exists(fullPath)) File.Delete(fullPath);
        }
        catch
        {
            // Компенсация не должна маскировать исходную ошибку записи.
        }
    }

    private string? SafePath(string? storedName)
    {
        if (string.IsNullOrWhiteSpace(storedName)) return null;
        var name = Path.GetFileName(storedName);
        if (name != storedName) return null;
        return Path.Combine(_root, name);
    }
}
