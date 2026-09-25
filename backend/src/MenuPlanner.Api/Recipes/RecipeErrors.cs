namespace MenuPlanner.Api.Recipes;

/// <summary>
/// Общие ответы-ошибки для операций над рецептами.
/// </summary>
public static class RecipeErrors
{
    /// <summary>
    /// Внешний рецепт доступен только для чтения: правки, удаление, фото и повторный
    /// шаринг запрещены, изменение контента остаётся за семьёй-источником.
    /// </summary>
    public static IResult ExternalReadOnly() =>
        Results.Json(
            new RecipeErrorDto("Внешний рецепт доступен только для чтения."),
            statusCode: StatusCodes.Status403Forbidden);
}
