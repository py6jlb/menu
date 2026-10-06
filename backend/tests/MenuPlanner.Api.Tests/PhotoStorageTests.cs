using MenuPlanner.Api.Recipes.Photos;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MenuPlanner.Api.Tests;

/// <summary>
/// Файловое хранилище: согласованное сохранение/чтение/удаление и терпимость к
/// конкурентно исчезнувшему источнику при копировании.
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
}
