using System.Text.RegularExpressions;

namespace MenuPlanner.Api.Recipes.Photos;

/// <summary>
/// Чистые правила имён файлов фото. Управляемое имя — один сегмент пути вида
/// "{recipeId:N}-{guid:N}{ext}" с поддерживаемым расширением. Уборка трогает
/// только такие файлы, поэтому посторонние объекты в каталоге не удаляются.
/// </summary>
public static partial class PhotoFileNames
{
    [GeneratedRegex(@"^[0-9a-f]{32}-[0-9a-f]{32}\.(jpg|png|webp|gif)$")]
    private static partial Regex ManagedName();

    public static bool IsManaged(string? name) =>
        !string.IsNullOrWhiteSpace(name) && ManagedName().IsMatch(name);
}
