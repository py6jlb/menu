using Xunit;
using MenuPlanner.Api.Recipes.External;

namespace MenuPlanner.Api.Tests;

public sealed class ExternalRecipeStateTests
{
    [Fact]
    public void Resolve_SourceAliveTokenMatchesNotRevoked_ReturnsOk()
    {
        Assert.Equal(
            ExternalRecipeState.Ok,
            ExternalRecipeStateRules.Resolve(sourceExists: true, tokenMatches: true, shareRevoked: false));
    }

    [Fact]
    public void Resolve_TokenMismatch_ReturnsWarning()
    {
        Assert.Equal(
            ExternalRecipeState.Warning,
            ExternalRecipeStateRules.Resolve(sourceExists: true, tokenMatches: false, shareRevoked: false));
    }

    [Fact]
    public void Resolve_ShareRevoked_ReturnsWarning()
    {
        Assert.Equal(
            ExternalRecipeState.Warning,
            ExternalRecipeStateRules.Resolve(sourceExists: true, tokenMatches: true, shareRevoked: true));
    }

    [Fact]
    public void Resolve_SourceMissing_ReturnsBroken()
    {
        Assert.Equal(
            ExternalRecipeState.Broken,
            ExternalRecipeStateRules.Resolve(sourceExists: false, tokenMatches: true, shareRevoked: false));
    }

    [Fact]
    public void Resolve_SourceMissingWithMismatch_StillReturnsBroken()
    {
        Assert.Equal(
            ExternalRecipeState.Broken,
            ExternalRecipeStateRules.Resolve(sourceExists: false, tokenMatches: false, shareRevoked: false));
    }

    [Theory]
    [InlineData(ExternalRecipeState.Ok, "ok")]
    [InlineData(ExternalRecipeState.Warning, "warning")]
    [InlineData(ExternalRecipeState.Broken, "broken")]
    public void Code_MapsStateToApiValue(ExternalRecipeState state, string expected)
    {
        Assert.Equal(expected, ExternalRecipeStateRules.Code(state));
    }
}
