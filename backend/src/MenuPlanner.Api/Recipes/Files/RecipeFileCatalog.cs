using System.Linq.Expressions;
using System.Text.RegularExpressions;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Recipes.Files;

/// <summary>
/// Дескриптор вида файла рецепта. Несёт ровно те отличия, которые задают вид:
/// ключ каталога конфигурации, regex управляемого имени, селектор ссылки в БД и
/// лог-префикс. Хранилище, жизненный цикл и уборка общие и параметризуются видом.
/// </summary>
public sealed record RecipeFileCatalog(
    RecipeFileKind Kind,
    string DirectoryKey,
    Regex ManagedName,
    Expression<Func<Recipe, string?>> Link,
    string LogPrefix)
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
        "Фото:");

    /// <summary>PDF-документ рецепта: хранится рядом с фото, но в своём каталоге.</summary>
    public static readonly RecipeFileCatalog Document = new(
        RecipeFileKind.Document,
        "RECIPE_FILES_DOCUMENTS_DIR",
        DocumentNames,
        r => r.DocumentPath,
        "Документ:");

    /// <summary>Все виды — для обхода в фоновой уборке и параметризованных тестов.</summary>
    public static IReadOnlyList<RecipeFileCatalog> All { get; } = new[] { Photo, Document };

    public static RecipeFileCatalog For(RecipeFileKind kind) => kind switch
    {
        RecipeFileKind.Photo => Photo,
        RecipeFileKind.Document => Document,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Неизвестный вид файла рецепта")
    };
}
