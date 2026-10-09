using System.Text;
using MenuPlanner.Api.Recipes.Documents;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MenuPlanner.Api.Tests;

public sealed class DocumentStorageTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "menu_planner_doc_store_" + Guid.NewGuid().ToString("N"));

    private DocumentStorage NewStorage()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DOCUMENTS_DIR"] = _root })
            .Build();
        return new DocumentStorage(configuration);
    }

    [Fact]
    public async Task Save_List_Resolve_Delete_RoundTrip()
    {
        var storage = NewStorage();
        var recipeId = Guid.NewGuid();

        var name = await storage.SaveAsync(recipeId, ".pdf", new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7")));

        Assert.StartsWith($"{recipeId:N}-", name);
        Assert.EndsWith(".pdf", name);
        Assert.Contains(storage.List(), f => f.Name == name);
        Assert.NotNull(storage.ResolveReadPath(name));

        storage.Delete(name);
        Assert.Null(storage.ResolveReadPath(name));
        Assert.DoesNotContain(storage.List(), f => f.Name == name);
    }

    [Fact]
    public void ResolveReadPath_RejectsTraversal()
    {
        var storage = NewStorage();
        Assert.Null(storage.ResolveReadPath("../secret.pdf"));
        Assert.Null(storage.ResolveReadPath("sub/dir.pdf"));
    }

    [Fact]
    public async Task Copy_MissingSource_ReturnsNull()
    {
        var storage = NewStorage();
        var copied = await storage.CopyAsync(Guid.NewGuid(), "0123456789abcdef0123456789abcdef-0123456789abcdef0123456789abcdef.pdf");
        Assert.Null(copied);
    }

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
}
