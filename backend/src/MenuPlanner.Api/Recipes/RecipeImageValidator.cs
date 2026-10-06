using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Recipes;

/// <summary>Фактический формат и размеры проверенного изображения.</summary>
public sealed record ValidatedPhoto(string Extension, int Width, int Height);

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

    public const string TooLargeDimensionsError =
        "Изображение слишком большое. Допустимо не более 8000 пикселей по стороне и 25 мегапикселей.";

    /// <summary>
    /// Разбирает поток как изображение. Размеры проверяются по заголовку до
    /// полного декодирования, чтобы малое по объёму изображение с огромными
    /// размерами не исчерпало ресурсы. Затем выполняется полное декодирование:
    /// обрезанный или повреждённый файл отклоняется. Поток должен поддерживать
    /// поиск (pos=0 используется перед декодированием).
    /// </summary>
    public static bool TryValidate(Stream stream, out ValidatedPhoto? photo, out string? error)
    {
        photo = null;
        error = null;

        try
        {
            var format = Image.DetectFormat(stream);
            if (format is null || !TryExtension(format, out var extension))
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
            // MaxFrames ограничивает ресурсы на многокадровых GIF; содержимое
            // не перекодируется, поэтому оригинал со всей анимацией и ориентацией
            // EXIF сохраняется без изменений.
            using var image = Image.Load(new DecoderOptions { MaxFrames = 1 }, stream);

            photo = new ValidatedPhoto(extension, image.Width, image.Height);
            return true;
        }
        catch (UnknownImageFormatException)
        {
            error = NotImageError;
            return false;
        }
        catch (InvalidImageContentException)
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
