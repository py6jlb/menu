using Xunit;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Tests;

public sealed class DietCatalogTests
{
    [Fact]
    public void Standard_HasCanonicalCodeAndRussianLabel()
    {
        Assert.Contains(DietCatalog.Standard, d => d.Code == "vegetarian" && d.Label == "Вегетарианское");
        Assert.Contains(DietCatalog.Standard, d => d.Code == "gluten_free" && d.Label == "Безглютеновое");
        Assert.Contains(DietCatalog.Standard, d => d.Code == "lean" && d.Label == "Постное");
        Assert.Contains(DietCatalog.Standard, d => d.Code == "keto" && d.Label == "Кетогенное");
    }

    [Theory]
    [InlineData("vegetarian", "vegetarian")]
    [InlineData("Vegetarian", "vegetarian")]
    [InlineData("ВЕГЕТАРИАНСКОЕ", "vegetarian")]
    [InlineData("Вегетарианский", "vegetarian")]
    [InlineData("безглютеновое", "gluten_free")]
    [InlineData("без глютена", "gluten_free")]
    [InlineData("Gluten-Free", "gluten_free")]
    [InlineData("постное", "lean")]
    [InlineData("Кетогенная", "keto")]
    public void Normalize_KnownVariant_ReturnsCanonicalCode(string value, string expected)
    {
        Assert.Equal(expected, DietCatalog.Normalize(value));
    }

    [Theory]
    [InlineData("моя диета")]
    [InlineData("Средиземноморское")]
    [InlineData("vegan")]
    public void Normalize_UnknownValue_IsPreservedNotTranslated(string value)
    {
        Assert.Equal(value, DietCatalog.Normalize(value));
    }

    [Fact]
    public void Normalize_TrimsWhitespaceOfUnknownValues()
    {
        Assert.Equal("моя диета", DietCatalog.Normalize("  моя диета  "));
    }

    [Fact]
    public void NormalizeAll_MapsVariantsDeduplicatesAndStaysIdempotent()
    {
        var once = DietCatalog.NormalizeAll(
            new[] { "Вегетарианское", "vegetarian", "моя диета", "постное", "Моя диета" });
        Assert.Equal(new[] { "vegetarian", "моя диета", "lean" }, once);

        var twice = DietCatalog.NormalizeAll(once);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void NormalizeAll_SkipsBlankValues()
    {
        Assert.Empty(DietCatalog.NormalizeAll(new[] { "", "   " }));
        Assert.Empty(DietCatalog.NormalizeAll(null));
    }
}
