namespace MenuPlanner.Api.Recipes.Files;

/// <summary>
/// Вид управляемого файла рецепта. Фото и PDF-документ отличаются каталогом,
/// допустимыми расширениями и полем ссылки в рецепте, но подчиняются одному
/// жизненному циклу и одной безопасной уборке.
/// </summary>
public enum RecipeFileKind
{
    Photo,
    Document
}
