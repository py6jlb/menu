using Xunit;
using MenuPlanner.Api.Recipes.External;

namespace MenuPlanner.Api.Tests;

public sealed class ExternalRecipeStateTests
{
    [Fact]
    public void Resolve_WhenSourceExists_ReturnsOk()
    {
        Assert.Equal(ExternalRecipeState.Ok, ExternalRecipeStateService.Resolve(sourceExists: true));
    }

    [Fact]
    public void Resolve_WhenSourceMissing_ReturnsBroken()
    {
        Assert.Equal(ExternalRecipeState.Broken, ExternalRecipeStateService.Resolve(sourceExists: false));
    }

    [Theory]
    [InlineData(ExternalRecipeState.Ok, "ok")]
    [InlineData(ExternalRecipeState.Warning, "warning")]
    [InlineData(ExternalRecipeState.Broken, "broken")]
    public void Code_MapsStateToApiValue(ExternalRecipeState state, string expected)
    {
        Assert.Equal(expected, ExternalRecipeStateService.Code(state));
    }
}
