using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MenuPlanner.Api.Tests;

/// <summary>
/// Реальные декодируемые изображения для HTTP-тестов загрузки фото: проверка
/// формата и размеров должна проходить на настоящем содержимом, а не на заглушке.
/// </summary>
internal static class TestImages
{
    public static byte[] Png(int width = 4, int height = 3) => Encode(width, height, (image, stream) => image.SaveAsPng(stream));

    public static byte[] Jpeg() => Encode(4, 3, (image, stream) => image.SaveAsJpeg(stream));

    public static byte[] Gif() => Encode(4, 3, (image, stream) => image.SaveAsGif(stream));

    public static byte[] Webp() => Encode(4, 3, (image, stream) => image.SaveAsWebp(stream));

    public static byte[] AnimatedGif()
    {
        using var image = new Image<Rgba32>(4, 3);
        image.Frames.AddFrame(image.Frames.RootFrame);
        using var stream = new MemoryStream();
        image.SaveAsGif(stream);
        return stream.ToArray();
    }

    private static byte[] Encode(int width, int height, Action<Image<Rgba32>, Stream> save)
    {
        using var image = new Image<Rgba32>(width, height);
        using var stream = new MemoryStream();
        save(image, stream);
        return stream.ToArray();
    }
}
