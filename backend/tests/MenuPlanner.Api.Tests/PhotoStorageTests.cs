using MenuPlanner.Api.Recipes.Photos;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MenuPlanner.Api.Tests;

/// <summary>
/// Файловое хранилище: согласованное сохранение/чтение/удаление, терпимость к
/// конкурентно исчезнувшему источнику при копировании и отсутствие частичного
/// файла при обрыве входного потока.
/// </summary>
public sealed class PhotoStorageTests
{
    [Fact]
    public async Task Save_List_ResolveDelete_RoundTrip()
    {
        var (store, root) = NewStore();
        var recipeId = Guid.NewGuid();

        var name = await store.SaveAsync(
            recipeId, ".png", new MemoryStream(new byte[] { 1, 2, 3 }));

        Assert.True(PhotoFileNames.IsManaged(name));
        Assert.Equal(Path.Combine(root, name), store.ResolveReadPath(name));
        Assert.Contains(store.List(), photo => photo.Name == name);

        store.Delete(name);
        Assert.Null(store.ResolveReadPath(name));
        Assert.DoesNotContain(store.List(), photo => photo.Name == name);
    }

    [Fact]
    public async Task CopyAsync_WhenSourceMissing_ReturnsNull()
    {
        var (store, _) = NewStore();
        Assert.Null(await store.CopyAsync(Guid.NewGuid(), "missing-source.png"));
    }

    [Fact]
    public async Task SaveAsync_WhenStreamFails_LeavesNoPartialFiles()
    {
        var (store, root) = NewStore();

        await Assert.ThrowsAnyAsync<IOException>(() =>
            store.SaveAsync(Guid.NewGuid(), ".png", new FailingStream()));

        Assert.Empty(Directory.GetFiles(root));
    }

    private static (PhotoStorage Storage, string Root) NewStore()
    {
        var root = Path.Combine(
            Path.GetTempPath(), "menu_planner_photo_storage_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PHOTOS_DIR"] = root })
            .Build();
        return (new PhotoStorage(configuration), root);
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
