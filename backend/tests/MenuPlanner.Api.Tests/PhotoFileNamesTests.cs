using MenuPlanner.Api.Recipes.Photos;
using Xunit;

namespace MenuPlanner.Api.Tests;

public sealed class PhotoFileNamesTests
{
    [Fact]
    public void IsManaged_MatchesNamesProducedByStorage()
    {
        var name = $"{Guid.NewGuid():N}-{Guid.NewGuid():N}.png";
        Assert.True(PhotoFileNames.IsManaged(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("photo.png")]
    [InlineData("not-a-photo.txt")]
    [InlineData("../escape.png")]
    [InlineData("0123456789abcdef0123456789abcdef-not-a-guid.png")]
    [InlineData("0123456789abcdef0123456789abcdef-0123456789abcdef0123456789abcdef.bmp")]
    public void IsManaged_RejectsForeignAndUnsupportedNames(string? name) =>
        Assert.False(PhotoFileNames.IsManaged(name));
}
