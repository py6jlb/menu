using System.Text;
using MenuPlanner.Api.Recipes;
using Xunit;

namespace MenuPlanner.Api.Tests;

public sealed class RecipePdfValidatorTests
{
    [Fact]
    public void Accepts_PdfSignature()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj"));
        Assert.True(RecipePdfValidator.TryValidate(stream));
    }

    [Fact]
    public void Rejects_NonPdfContent()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("просто текст"));
        Assert.False(RecipePdfValidator.TryValidate(stream));
    }

    [Fact]
    public void Rejects_TruncatedSignature()
    {
        using var stream = new MemoryStream(new byte[] { 0x25, 0x50, 0x44 });
        Assert.False(RecipePdfValidator.TryValidate(stream));
    }

    [Fact]
    public void LeavesStreamPositionAtStart()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7"));
        RecipePdfValidator.TryValidate(stream);
        Assert.Equal(0, stream.Position);
    }
}
