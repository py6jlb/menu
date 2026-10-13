using MenuPlanner.Api.Features.Recipes.Files;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MenuPlanner.Api.Tests.Features.Recipes;

/// <summary>
/// Файловое хранилище, параметризованное видом: согласованное
/// сохранение/чтение/удаление, терпимость к конкурентно исчезнувшему источнику при
/// копировании, отсутствие частичного файла при обрыве потока и fallback на старые
/// env-ключи каталога.
/// </summary>
public sealed class FileStorageTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "menu_planner_file_storage_" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(RecipeFileKind.Photo, ".png")]
    [InlineData(RecipeFileKind.Document, ".pdf")]
    public async Task Save_List_ResolveDelete_RoundTrip(RecipeFileKind kind, string extension)
    {
        var store = NewStorage();
        var recipeId = Guid.NewGuid();

        var name = await store.SaveAsync(kind, recipeId, extension, new MemoryStream(new byte[] { 1, 2, 3 }));

        Assert.True(RecipeFileNames.IsManaged(kind, name));
        Assert.Equal(Path.Combine(KindRoot(kind), name), store.ResolveReadPath(kind, name));
        Assert.Contains(store.List(kind), file => file.Name == name);

        store.Delete(kind, name);
        Assert.Null(store.ResolveReadPath(kind, name));
        Assert.DoesNotContain(store.List(kind), file => file.Name == name);
    }

    [Theory]
    [InlineData(RecipeFileKind.Photo, "missing-source.png")]
    [InlineData(RecipeFileKind.Document, "missing-source.pdf")]
    public async Task CopyAsync_WhenSourceMissing_ReturnsNull(RecipeFileKind kind, string sourceName)
    {
        var store = NewStorage();
        Assert.Null(await store.CopyAsync(kind, Guid.NewGuid(), sourceName));
    }

    [Theory]
    [InlineData(RecipeFileKind.Photo, ".png")]
    [InlineData(RecipeFileKind.Document, ".pdf")]
    public async Task SaveAsync_WhenStreamFails_LeavesNoPartialFiles(RecipeFileKind kind, string extension)
    {
        var store = NewStorage();

        await Assert.ThrowsAnyAsync<IOException>(() =>
            store.SaveAsync(kind, Guid.NewGuid(), extension, new FailingStream()));

        Assert.Empty(Directory.GetFiles(KindRoot(kind)));
    }

    [Theory]
    [InlineData(RecipeFileKind.Photo)]
    [InlineData(RecipeFileKind.Document)]
    public void ResolveReadPath_RejectsTraversal(RecipeFileKind kind)
    {
        var store = NewStorage();
        Assert.Null(store.ResolveReadPath(kind, "../secret.pdf"));
        Assert.Null(store.ResolveReadPath(kind, "sub/dir.pdf"));
    }

    [Theory]
    [InlineData(RecipeFileKind.Photo, "PHOTOS_DIR")]
    [InlineData(RecipeFileKind.Document, "DOCUMENTS_DIR")]
    public void OldDirectoryKey_IsReadAsFallback(RecipeFileKind kind, string legacyKey)
    {
        var legacyRoot = Path.Combine(_root, "legacy-" + kind);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [legacyKey] = legacyRoot })
            .Build();

        _ = new FileStorage(configuration, NullLogger<FileStorage>.Instance);

        Assert.True(Directory.Exists(legacyRoot));
    }

    private FileStorage NewStorage()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RECIPE_FILES_PHOTOS_DIR"] = Path.Combine(_root, "photos"),
                ["RECIPE_FILES_DOCUMENTS_DIR"] = Path.Combine(_root, "documents")
            })
            .Build();
        return new FileStorage(configuration, NullLogger<FileStorage>.Instance);
    }

    private string KindRoot(RecipeFileKind kind) =>
        Path.Combine(_root, kind == RecipeFileKind.Photo ? "photos" : "documents");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
            }
        }
    }

    private sealed class FailingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("обрыв");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("обрыв"));

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
