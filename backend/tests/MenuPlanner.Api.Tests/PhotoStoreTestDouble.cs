using MenuPlanner.Api.Recipes.Photos;

namespace MenuPlanner.Api.Tests;

/// <summary>
/// Хранилище фото в памяти: тесты управляют содержимым, временем и сбоями
/// удаления, не трогая диск.
/// </summary>
internal sealed class InMemoryPhotoStore : IPhotoStore
{
    private readonly Dictionary<string, Entry> _files = new(StringComparer.Ordinal);

    /// <summary>Сколько раз вызывалось удаление (включая неуспешные).</summary>
    public int DeleteCalls { get; private set; }

    /// <summary>Удаление имени, для которого предикат истинен, падает.</summary>
    public Func<string, bool>? FailingDelete { get; set; }

    public IReadOnlyList<string> Names => _files.Keys.ToList();

    public void Put(string name, byte[]? content = null, DateTime? lastWriteUtc = null) =>
        _files[name] = new Entry(content ?? Array.Empty<byte>(), lastWriteUtc ?? DateTime.UtcNow);

    public bool Contains(string name) => _files.ContainsKey(name);

    public async Task<string> SaveAsync(
        Guid recipeId, string extension, Stream stream, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        var name = $"{recipeId:N}-{Guid.NewGuid():N}{extension}";
        _files[name] = new Entry(buffer.ToArray(), DateTime.UtcNow);
        return name;
    }

    public Task<string?> CopyAsync(
        Guid recipeId, string? sourceStoredName, CancellationToken cancellationToken = default)
    {
        if (sourceStoredName is null || !_files.TryGetValue(sourceStoredName, out var source))
            return Task.FromResult<string?>(null);

        var name = $"{recipeId:N}-{Guid.NewGuid():N}{Path.GetExtension(sourceStoredName)}";
        _files[name] = new Entry(source.Content, DateTime.UtcNow);
        return Task.FromResult<string?>(name);
    }

    public void Delete(string? storedName)
    {
        if (storedName is null) return;
        DeleteCalls++;
        if (FailingDelete?.Invoke(storedName) == true)
            throw new IOException($"Сбой удаления {storedName}");
        _files.Remove(storedName);
    }

    public string? ResolveReadPath(string? fileName) => null;

    public IReadOnlyList<StoredPhoto> List() =>
        _files.Select(pair => new StoredPhoto(pair.Key, pair.Value.LastWriteUtc)).ToList();

    private sealed record Entry(byte[] Content, DateTime LastWriteUtc);
}
