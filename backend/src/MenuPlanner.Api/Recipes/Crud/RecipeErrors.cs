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

    /// <summary>Сохранение основано на устаревшей ревизии: черновик сохраняется у клиента.</summary>
    public static IResult RevisionConflict(int currentRevision) =>
        Results.Conflict(new RecipeConflictDto(RecipeRevisionRules.ConflictMessage, currentRevision));

    /// <summary>Клиент не передал ожидаемую ревизию.</summary>
    public static IResult MissingRevision() =>
        Results.BadRequest(new RecipeValidationErrorDto(
            RecipeRevisionRules.MissingMessage, "revision_missing"));

    /// <summary>
    /// Ошибка ввода: русский текст, машинный код и путь поля, чтобы клиент показал
    /// причину у нужного поля и сохранил черновик.
    /// </summary>
    public static IResult Validation(RecipeFieldError error) =>
        Results.BadRequest(new RecipeValidationErrorDto(error.Message, error.Code, error.Field));
}
