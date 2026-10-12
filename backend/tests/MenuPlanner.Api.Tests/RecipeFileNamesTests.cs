using MenuPlanner.Api.Recipes.Files;
using Xunit;

namespace MenuPlanner.Api.Tests;

/// <summary>
/// Чистые правила имён файлов рецептов, параметризованные видом: имя, которое
/// производит хранилище, управляемо, а посторонние/чужие расширения и выход из
/// каталога — нет.
/// </summary>
public sealed class RecipeFileNamesTests
{
    [Theory]
    [InlineData(RecipeFileKind.Photo, "0123456789abcdef0123456789abcdef-0123456789abcdef0123456789abcdef.png")]
    [InlineData(RecipeFileKind.Photo, "0123456789abcdef0123456789abcdef-0123456789abcdef0123456789abcdef.jpg")]
    [InlineData(RecipeFileKind.Photo, "0123456789abcdef0123456789abcdef-0123456789abcdef0123456789abcdef.webp")]
    [InlineData(RecipeFileKind.Photo, "0123456789abcdef0123456789abcdef-0123456789abcdef0123456789abcdef.gif")]
    [InlineData(RecipeFileKind.Document, "0123456789abcdef0123456789abcdef-0123456789abcdef0123456789abcdef.pdf")]
    public void IsManaged_AcceptsProducedManagedNames(RecipeFileKind kind, string name) =>
        Assert.True(RecipeFileNames.IsManaged(kind, name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("photo.png")]
    [InlineData("not-a-photo.txt")]
    [InlineData("../escape.png")]
    [InlineData("sub/dir.pdf")]
    [InlineData("0123456789abcdef0123456789abcdef-not-a-guid.png")]
    [InlineData("0123456789abcdef0123456789abcdef-0123456789abcdef0123456789abcdef.bmp")]
    [InlineData("0123456789abcdef0123456789abcdef-0123456789abcdef0123456789abcdef.PDF")]
    public void IsManaged_RejectsForeignAndUnsupportedNames(string? name)
    {
        Assert.False(RecipeFileNames.IsManaged(RecipeFileKind.Photo, name));
        Assert.False(RecipeFileNames.IsManaged(RecipeFileKind.Document, name));
    }

    [Theory]
    [InlineData(RecipeFileKind.Photo, "0123456789abcdef0123456789abcdef-0123456789abcdef0123456789abcdef.pdf")]
    [InlineData(RecipeFileKind.Document, "0123456789abcdef0123456789abcdef-0123456789abcdef0123456789abcdef.png")]
    public void IsManaged_RejectsOtherKindExtension(RecipeFileKind kind, string name) =>
        Assert.False(RecipeFileNames.IsManaged(kind, name));
}
