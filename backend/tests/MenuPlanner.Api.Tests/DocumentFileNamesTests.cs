using MenuPlanner.Api.Recipes.Documents;
using Xunit;

namespace MenuPlanner.Api.Tests;

public sealed class DocumentFileNamesTests
{
    [Fact]
    public void Accepts_ProducedManagedName()
    {
        var name = $"{Guid.NewGuid():N}-{Guid.NewGuid():N}.pdf";
        Assert.True(DocumentFileNames.IsManaged(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("readme.txt")]
    [InlineData("0123456789abcdef0123456789abcdef-0123456789abcdef0123456789abcdef.png")]
    [InlineData("../escape.pdf")]
    [InlineData("sub/dir.pdf")]
    [InlineData("0123456789abcdef0123456789abcdef-0123456789abcdef0123456789abcdef.PDF")]
    public void Rejects_ForeignOrTraversalNames(string? name)
    {
        Assert.False(DocumentFileNames.IsManaged(name));
    }
}
