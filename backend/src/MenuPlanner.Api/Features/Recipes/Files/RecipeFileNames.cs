namespace MenuPlanner.Api.Features.Recipes.Files;

/// <summary>
/// Чистые правила имён файлов рецептов. Управляемое имя — один сегмент пути вида
/// "{recipeId:N}-{guid:N}{ext}" с поддерживаемым для вида расширением. Уборка
/// трогает только такие файлы, поэтому посторонние объекты в каталоге не удаляются.
/// </summary>
public static class RecipeFileNames
{
    public static bool IsManaged(RecipeFileKind kind, string? name) =>
        !string.IsNullOrWhiteSpace(name) && RecipeFileCatalog.For(kind).ManagedName.IsMatch(name);
}
