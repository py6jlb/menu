using System.Text;
using MenuPlanner.Api.Recipes;
using MenuPlanner.Api.Recipes.Files;
using MenuPlanner.Api.Tests.Support;
using Microsoft.AspNetCore.Http;
using Xunit;
using MenuPlanner.Domain.Recipes;

namespace MenuPlanner.Api.Tests.Recipes;

/// <summary>
/// Политика приёма файла рецепта на фейковом <see cref="IFormFile"/>: размер,
/// формат (фото/PDF), битый файл и корректный файл. Проверяем исход, определённое
/// по содержимому расширение и готовый текст ошибки, не поднимая HTTP-слой.
/// </summary>
public sealed class RecipeFileUploadTests
{
    [Fact]
    public async Task NullFile_ReturnsEmptyError()
    {
        var result = await RecipeFileUpload.ReadAsync(RecipeFileCatalog.Photo, null);

        Assert.Equal(RecipeFileUploadOutcome.Empty, result.Outcome);
        Assert.False(result.Succeeded);
        Assert.Equal("Выберите файл изображения.", result.Error);
        Assert.Null(result.Content);
    }

    [Fact]
    public async Task EmptyDocument_ReturnsEmptyError()
    {
        var result = await RecipeFileUpload.ReadAsync(
            RecipeFileCatalog.Document, new FakeFormFile(Array.Empty<byte>()));

        Assert.Equal(RecipeFileUploadOutcome.Empty, result.Outcome);
        Assert.Equal("Выберите PDF-файл.", result.Error);
    }

    [Fact]
    public async Task Photo_TooLarge_ReturnsTooLargeError()
    {
        // Длины достаточно для отказа: содержимое не читается.
        var file = new FakeFormFile(Array.Empty<byte>(), length: RecipeCatalog.PhotoMaxBytes + 1);

        var result = await RecipeFileUpload.ReadAsync(RecipeFileCatalog.Photo, file);

        Assert.Equal(RecipeFileUploadOutcome.TooLarge, result.Outcome);
        Assert.Equal("Размер фото не должен превышать 5 МБ.", result.Error);
        Assert.Null(result.Content);
    }

    [Fact]
    public async Task Document_TooLarge_ReturnsTooLargeError()
    {
        var file = new FakeFormFile(Array.Empty<byte>(), length: RecipeCatalog.DocumentMaxBytes + 1);

        var result = await RecipeFileUpload.ReadAsync(RecipeFileCatalog.Document, file);

        Assert.Equal(RecipeFileUploadOutcome.TooLarge, result.Outcome);
        Assert.Equal("Размер PDF не должен превышать 20 МБ.", result.Error);
    }

    [Fact]
    public async Task Photo_NotImage_ReturnsNotImageError()
    {
        var file = new FakeFormFile(Encoding.UTF8.GetBytes("это просто текст, а не изображение"));

        var result = await RecipeFileUpload.ReadAsync(RecipeFileCatalog.Photo, file);

        Assert.Equal(RecipeFileUploadOutcome.InvalidContent, result.Outcome);
        Assert.Equal(RecipeImageValidator.NotImageError, result.Error);
        Assert.Null(result.Content);
    }

    [Fact]
    public async Task Document_NotPdf_ReturnsNotPdfError()
    {
        var file = new FakeFormFile(Encoding.ASCII.GetBytes("это не pdf"));

        var result = await RecipeFileUpload.ReadAsync(RecipeFileCatalog.Document, file);

        Assert.Equal(RecipeFileUploadOutcome.InvalidContent, result.Outcome);
        Assert.Equal(RecipePdfValidator.NotPdfError, result.Error);
        Assert.Null(result.Content);
    }

    [Fact]
    public async Task Photo_Corrupted_ReturnsNotImageError()
    {
        var valid = TestImages.Png(32, 32);
        var corrupted = valid.AsSpan(0, valid.Length / 2).ToArray();

        var result = await RecipeFileUpload.ReadAsync(RecipeFileCatalog.Photo, new FakeFormFile(corrupted));

        Assert.Equal(RecipeFileUploadOutcome.InvalidContent, result.Outcome);
        Assert.Equal(RecipeImageValidator.NotImageError, result.Error);
    }

    [Fact]
    public async Task Photo_Valid_ReturnsSeekableStreamAtStartAndExtension()
    {
        var bytes = TestImages.Png();

        var result = await RecipeFileUpload.ReadAsync(RecipeFileCatalog.Photo, new FakeFormFile(bytes));

        Assert.True(result.Succeeded);
        Assert.Equal(".png", result.Extension);
        var content = Assert.IsAssignableFrom<MemoryStream>(result.Content);
        Assert.True(content.CanSeek);
        Assert.Equal(0, content.Position);
        Assert.Equal(bytes, await ReadAllAsync(content));
    }

    [Fact]
    public async Task Document_Valid_ReturnsPdfExtension()
    {
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<<>>\nendobj\ntrailer\n%%EOF");

        var result = await RecipeFileUpload.ReadAsync(RecipeFileCatalog.Document, new FakeFormFile(bytes));

        Assert.True(result.Succeeded);
        Assert.Equal(".pdf", result.Extension);
        Assert.Equal(bytes, await ReadAllAsync(result.Content!));
    }

    [Fact]
    public async Task Photo_FormatFromContent_NotDeclaredNameOrMime()
    {
        // Реальные байты PNG, объявлены и названы как JPEG: формат берётся из содержимого.
        var file = new FakeFormFile(
            TestImages.Png(), fileName: "photo.jpg", contentType: "image/jpeg");

        var result = await RecipeFileUpload.ReadAsync(RecipeFileCatalog.Photo, file);

        Assert.True(result.Succeeded);
        Assert.Equal(".png", result.Extension);
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }
}

/// <summary>Фейковый <see cref="IFormFile"/>: отдаёт заданные байты без HTTP-слоя.</summary>
internal sealed class FakeFormFile : IFormFile
{
    private readonly byte[] _content;

    public FakeFormFile(
        byte[] content,
        long? length = null,
        string fileName = "file.bin",
        string contentType = "application/octet-stream")
    {
        _content = content;
        Length = length ?? content.Length;
        FileName = fileName;
        ContentType = contentType;
    }

    public string ContentType { get; }

    public string ContentDisposition => $"form-data; name=\"file\"; filename=\"{FileName}\"";

    public IHeaderDictionary Headers { get; } = new HeaderDictionary();

    public long Length { get; }

    public string Name => "file";

    public string FileName { get; }

    public void CopyTo(Stream target) => target.Write(_content, 0, _content.Length);

    public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
    {
        CopyTo(target);
        return Task.CompletedTask;
    }

    public Stream OpenReadStream() => new MemoryStream(_content, writable: false);
}
