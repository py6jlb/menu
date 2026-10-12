using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MenuPlanner.Api.Recipes.Files;

/// <summary>
/// Файловое хранилище файлов рецептов на диске сервера. Каждый вид живёт в своём
/// каталоге. Новая запись защищена от неполного файла: при сбое посреди копирования
/// частичный файл удаляется. Копирование терпимо к исчезнувшему источнику
/// (конкурентная замена) и тогда не создаёт файл, а не падает. Singleton-сервис.
/// </summary>
public sealed class FileStorage : IRecipeFileStore
{
    private readonly IReadOnlyDictionary<RecipeFileKind, string> _roots;
    private readonly ILogger<FileStorage> _logger;

    public FileStorage(IConfiguration configuration, ILogger<FileStorage> logger)
    {
        _logger = logger;
        var roots = new Dictionary<RecipeFileKind, string>();
        foreach (var catalog in RecipeFileCatalog.All)
        {
            var root = Path.GetFullPath(ResolveRoot(configuration, catalog));
            Directory.CreateDirectory(root);
            roots[catalog.Kind] = root;
        }

        _roots = roots;
    }

    public async Task<string> SaveAsync(
        RecipeFileKind kind,
        Guid recipeId,
        string extension,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var root = _roots[kind];
        var fileName = $"{recipeId:N}-{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(root, fileName);
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
        RecipeFileKind kind,
        Guid recipeId,
        string? sourceStoredName,
        CancellationToken cancellationToken = default)
    {
        var sourcePath = ResolveReadPath(kind, sourceStoredName);
        if (sourcePath is null) return null;
        var extension = Path.GetExtension(sourcePath);
        try
        {
            await using var stream = File.OpenRead(sourcePath);
            return await SaveAsync(kind, recipeId, extension, stream, cancellationToken);
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

    public void Delete(RecipeFileKind kind, string? storedName)
    {
        var fullPath = SafePath(kind, storedName);
        if (fullPath is null || !File.Exists(fullPath)) return;
        File.Delete(fullPath);
    }

    public string? ResolveReadPath(RecipeFileKind kind, string? fileName)
    {
        var fullPath = SafePath(kind, fileName);
        return fullPath is not null && File.Exists(fullPath) ? fullPath : null;
    }

    public IReadOnlyList<StoredFile> List(RecipeFileKind kind)
    {
        var root = _roots[kind];
        if (!Directory.Exists(root)) return Array.Empty<StoredFile>();
        return Directory.EnumerateFiles(root)
            .Select(path => new StoredFile(Path.GetFileName(path), File.GetLastWriteTimeUtc(path)))
            .ToList();
    }

    /// <summary>
    /// Каталог вида из канонического ключа; старый ключ читается как fallback с
    /// записью о deprecation, чтобы обновление развёрнутого стенда не подменило
    /// каталог молча.
    /// </summary>
    private string ResolveRoot(IConfiguration configuration, RecipeFileCatalog catalog)
    {
        var dir = configuration[catalog.DirectoryKey];
        if (!string.IsNullOrWhiteSpace(dir)) return dir;

        var legacyKey = LegacyDirectoryKey(catalog.Kind);
        var legacy = configuration[legacyKey];
        if (!string.IsNullOrWhiteSpace(legacy))
        {
            _logger.LogWarning(
                "Конфигурация: {LegacyKey} устарел, используйте {DirectoryKey}. Прочитано значение {Value}.",
                legacyKey, catalog.DirectoryKey, legacy);
            return legacy;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), DefaultDirectoryName(catalog.Kind));
    }

    private static string LegacyDirectoryKey(RecipeFileKind kind) => kind switch
    {
        RecipeFileKind.Photo => "PHOTOS_DIR",
        RecipeFileKind.Document => "DOCUMENTS_DIR",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Неизвестный вид файла рецепта")
    };

    private static string DefaultDirectoryName(RecipeFileKind kind) => kind switch
    {
        RecipeFileKind.Photo => "photos",
        RecipeFileKind.Document => "documents",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Неизвестный вид файла рецепта")
    };

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

    private string? SafePath(RecipeFileKind kind, string? storedName)
    {
        if (string.IsNullOrWhiteSpace(storedName)) return null;
        var name = Path.GetFileName(storedName);
        if (name != storedName) return null;
        return Path.Combine(_roots[kind], name);
    }
}
