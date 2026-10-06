using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Recipes;

/// <summary>
/// Проверяет, что загруженный файл — действительно декодируемое изображение
/// поддерживаемого формата, и что размеры укладываются в лимиты. Формат
/// определяется по содержимому, а не по присланному ContentType/расширению,
/// поэтому тип выдачи всегда соответствует реально сохранённым байтам.
/// </summary>
public static class RecipeImageValidator
{
    public const string NotImageError =
        "Файл не является изображением или повреждён. Допустимы JPEG, PNG, WebP и GIF.";

    public static readonly string TooLargeDimensionsError =
        $"Изображение слишком большое. Допустимо не более {RecipeCatalog.PhotoMaxDimension} пикселей "
        + $"по стороне и {RecipeCatalog.PhotoMaxPixels / 1_000_000} мегапикселей.";

    /// <summary>
    /// Разбирает поток как изображение и возвращает фактическое расширение.
    /// Размеры проверяются по заголовку до декодирования пикселей, чтобы малое
    /// по объёму изображение с огромными размерами не исчерпало ресурсы. Затем
    /// декодируется первый кадр: обрезанный или повреждённый файл отклоняется.
    /// Поток должен поддерживать поиск (перед разбором выполняется pos=0).
    /// </summary>
    public static bool TryValidate(Stream stream, out string? extension, out string? error)
    {
        extension = null;
        error = null;

        try
        {
            var format = Image.DetectFormat(stream);
            if (format is null || !TryExtension(format, out var detected))
            {
                error = NotImageError;
                return false;
            }

            if (stream.CanSeek) stream.Position = 0;
            var info = Image.Identify(stream);
            if (info is null)
            {
                error = NotImageError;
                return false;
            }

            if (info.Width > RecipeCatalog.PhotoMaxDimension
                || info.Height > RecipeCatalog.PhotoMaxDimension
                || (long)info.Width * info.Height > RecipeCatalog.PhotoMaxPixels)
            {
                error = TooLargeDimensionsError;
                return false;
            }

            if (stream.CanSeek) stream.Position = 0;
            // MaxFrames=1 ограничивает расходы на многокадровых изображениях
            // (проверяется первый кадр). Оригинал не перекодируется, поэтому
            // анимация, метаданные и ориентация EXIF сохраняются без изменений.
            using var image = Image.Load(new DecoderOptions { MaxFrames = 1 }, stream);

            extension = detected;
            return true;
        }
        catch (ImageFormatException)
        {
            error = NotImageError;
            return false;
        }
        catch (NotSupportedException)
        {
            error = NotImageError;
            return false;
        }
    }

    private static bool TryExtension(IImageFormat format, out string extension)
    {
        if (RecipeCatalog.PhotoContentTypes.TryGetValue(format.DefaultMimeType, out var found))
        {
            extension = found;
            return true;
        }

        extension = string.Empty;
        return false;
    }
}
