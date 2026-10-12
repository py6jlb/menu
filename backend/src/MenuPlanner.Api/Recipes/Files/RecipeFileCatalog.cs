using System.Linq.Expressions;
using System.Text.RegularExpressions;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Recipes.Files;

/// <summary>
/// Проба содержимого вида: разбирает поток как файл вида и возвращает определённое
/// по содержимому расширение или готовый текст ошибки. Реализуется байтовым
/// валидатором вида (<see cref="RecipeImageValidator"/> / <see cref="RecipePdfValidator"/>).
/// </summary>
public delegate bool RecipeFileContentProbe(Stream content, out string? extension, out string? error);

/// <summary>
/// Дескриптор вида файла рецепта. Несёт ровно те отличия, которые задают вид:
/// ключ каталога конфигурации, regex управляемого имени, селектор ссылки в БД,
/// лог-префикс, а также правила загрузки — максимум байт, тексты ошибок выбора и
/// превышения размера и пробу содержимого. Хранилище, жизненный цикл и уборка
/// общие и параметризуются видом; правила загрузки живут здесь, чтобы у вида был
/// один источник и для lifecycle/уборки, и для приёма файла.
/// </summary>
public sealed record RecipeFileCatalog(
    RecipeFileKind Kind,
    string DirectoryKey,
    Regex ManagedName,
    Expression<Func<Recipe, string?>> Link,
    string LogPrefix,
    int MaxBytes,
    string EmptyError,
    string TooLargeError,
    RecipeFileContentProbe Probe)
{
    private static readonly Regex PhotoNames =
        new(@"^[0-9a-f]{32}-[0-9a-f]{32}\.(jpg|png|webp|gif)$", RegexOptions.Compiled);

    private static readonly Regex DocumentNames =
        new(@"^[0-9a-f]{32}-[0-9a-f]{32}\.pdf$", RegexOptions.Compiled);

    /// <summary>Фото рецепта: изображение, отдаётся анонимно по неугадываемому имени.</summary>
    public static readonly RecipeFileCatalog Photo = new(
        RecipeFileKind.Photo,
        "RECIPE_FILES_PHOTOS_DIR",
        PhotoNames,
        r => r.PhotoPath,
        "Фото:",
        RecipeCatalog.PhotoMaxBytes,
        "Выберите файл изображения.",
        $"Размер фото не должен превышать {RecipeCatalog.PhotoMaxBytes / (1024 * 1024)} МБ.",
        RecipeImageValidator.TryValidate);

    /// <summary>PDF-документ рецепта: хранится рядом с фото, но в своём каталоге.</summary>
    public static readonly RecipeFileCatalog Document = new(
        RecipeFileKind.Document,
        "RECIPE_FILES_DOCUMENTS_DIR",
        DocumentNames,
        r => r.DocumentPath,
        "Документ:",
        RecipeCatalog.DocumentMaxBytes,
        "Выберите PDF-файл.",
        $"Размер PDF не должен превышать {RecipeCatalog.DocumentMaxBytes / (1024 * 1024)} МБ.",
        ProbeDocument);

    /// <summary>
    /// Проба PDF по сигнатуре; расширение вида одно — <c>.pdf</c>. Ошибка — общий
    /// текст валидатора, чтобы HTTP-контракт не менялся.
    /// </summary>
    private static bool ProbeDocument(Stream content, out string? extension, out string? error)
    {
        if (RecipePdfValidator.TryValidate(content))
        {
            extension = ".pdf";
            error = null;
            return true;
        }

        extension = null;
        error = RecipePdfValidator.NotPdfError;
        return false;
    }

    /// <summary>Все виды — для обхода в фоновой уборке и параметризованных тестов.</summary>
    public static IReadOnlyList<RecipeFileCatalog> All { get; } = new[] { Photo, Document };

    public static RecipeFileCatalog For(RecipeFileKind kind) => kind switch
    {
        RecipeFileKind.Photo => Photo,
        RecipeFileKind.Document => Document,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Неизвестный вид файла рецепта")
    };
}
