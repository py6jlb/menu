using System.Text.RegularExpressions;

namespace MenuPlanner.Api.Recipes.Documents;

/// <summary>
/// Чистые правила имён файлов документов. Управляемое имя — один сегмент пути вида
/// "{recipeId:N}-{guid:N}.pdf". Уборка трогает только такие файлы, поэтому
/// посторонние объекты в каталоге не удаляются.
/// </summary>
public static partial class DocumentFileNames
{
    [GeneratedRegex(@"^[0-9a-f]{32}-[0-9a-f]{32}\.pdf$")]
    private static partial Regex ManagedName();

    public static bool IsManaged(string? name) =>
        !string.IsNullOrWhiteSpace(name) && ManagedName().IsMatch(name);
}
