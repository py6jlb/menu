using MenuPlanner.Api.Recipes.Files;

namespace MenuPlanner.Api.Tests;

/// <summary>
/// Хранилище файлов рецептов в памяти на оба вида: тесты управляют содержимым,
/// временем и сбоями удаления, не трогая диск.
/// </summary>
internal sealed class InMemoryRecipeFileStore : IRecipeFileStore
{
    private readonly Dictionary<RecipeFileKind, Dictionary<string, Entry>> _files = new()
    {
        [RecipeFileKind.Photo] = new(StringComparer.Ordinal),
        [RecipeFileKind.Document] = new(StringComparer.Ordinal)
    };

    /// <summary>Сколько раз вызывалось удаление (включая неуспешные).</summary>
    public int DeleteCalls { get; private set; }

    /// <summary>Удаление имени вида, для которого предикат истинен, падает.</summary>
    public Func<RecipeFileKind, string, bool>? FailingDelete { get; set; }

    public IReadOnlyList<string> Names(RecipeFileKind kind) => _files[kind].Keys.ToList();

    public void Put(RecipeFileKind kind, string name, byte[]? content = null, DateTime? lastWriteUtc = null) =>
        _files[kind][name] = new Entry(content ?? Array.Empty<byte>(), lastWriteUtc ?? DateTime.UtcNow);

    public bool Contains(RecipeFileKind kind, string name) => _files[kind].ContainsKey(name);

    public async Task<string> SaveAsync(
        RecipeFileKind kind,
        Guid recipeId,
        string extension,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        var name = $"{recipeId:N}-{Guid.NewGuid():N}{extension}";
        _files[kind][name] = new Entry(buffer.ToArray(), DateTime.UtcNow);
        return name;
    }

    public Task<string?> CopyAsync(
        RecipeFileKind kind,
        Guid recipeId,
        string? sourceStoredName,
        CancellationToken cancellationToken = default)
    {
        if (sourceStoredName is null || !_files[kind].TryGetValue(sourceStoredName, out var source))
            return Task.FromResult<string?>(null);

        var name = $"{recipeId:N}-{Guid.NewGuid():N}{Path.GetExtension(sourceStoredName)}";
        _files[kind][name] = new Entry(source.Content, DateTime.UtcNow);
        return Task.FromResult<string?>(name);
    }

    public void Delete(RecipeFileKind kind, string? storedName)
    {
        if (storedName is null) return;
        DeleteCalls++;
        if (FailingDelete?.Invoke(kind, storedName) == true)
            throw new IOException($"Сбой удаления {storedName}");
        _files[kind].Remove(storedName);
    }

    public string? ResolveReadPath(RecipeFileKind kind, string? fileName) => null;

    public IReadOnlyList<StoredFile> List(RecipeFileKind kind) =>
        _files[kind].Select(pair => new StoredFile(pair.Key, pair.Value.LastWriteUtc)).ToList();

    private sealed record Entry(byte[] Content, DateTime LastWriteUtc);
}
