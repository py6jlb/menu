namespace MenuPlanner.Api.Features.Recipes.Files;

/// <summary>Исход приёма файла рецепта: успех или причина отказа для HTTP-ответа.</summary>
public enum RecipeFileUploadOutcome
{
    Ok,
    Empty,
    TooLarge,
    InvalidContent
}

/// <summary>
/// Результат приёма файла рецепта. На успехе (<see cref="Succeeded"/>) несёт
/// seekable-поток с содержимым и определённое по содержимому расширение; на отказе —
/// готовый текст ошибки для <c>RecipeErrorDto</c>.
/// </summary>
public sealed record RecipeFileUploadResult(
    RecipeFileUploadOutcome Outcome,
    Stream? Content = null,
    string? Extension = null,
    string? Error = null)
{
    public bool Succeeded => Outcome == RecipeFileUploadOutcome.Ok;
}

/// <summary>
/// Политика приёма файла рецепта: размер <see cref="IFormFile"/> против максимума
/// вида, чтение содержимого в <em>один</em> seekable-поток и проба содержимого по
/// правилам вида. Правила (максимум байт, валидатор, определение расширения и
/// тексты ошибок) берутся из дескриптора <see cref="RecipeFileCatalog"/> — того же
/// источника, что задаёт каталог и уборку вида. Формат определяется по содержимому,
/// а не по ContentType/имени файла, поэтому поддельный MIME и повреждённый файл
/// отклоняются до записи в БД. На успехе поток передаётся дальше как есть, без
/// промежуточного <c>byte[]</c>.
/// </summary>
public static class RecipeFileUpload
{
    public static async Task<RecipeFileUploadResult> ReadAsync(
        RecipeFileCatalog catalog,
        IFormFile? file,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
            return new(RecipeFileUploadOutcome.Empty, Error: catalog.EmptyError);

        if (file.Length > catalog.MaxBytes)
            return new(RecipeFileUploadOutcome.TooLarge, Error: catalog.TooLargeError);

        // Один seekable-поток на всю загрузку: в него читается содержимое, в нём же
        // пробуется формат, и он же уходит в хранилище.
        await using var source = file.OpenReadStream();
        var content = new MemoryStream();
        await source.CopyToAsync(content, cancellationToken);
        content.Position = 0;

        if (!catalog.Probe(content, out var extension, out var error))
        {
            await content.DisposeAsync();
            return new(RecipeFileUploadOutcome.InvalidContent, Error: error);
        }

        // Проба могла сдвинуть курсор (ImageSharp читает поток): дальше поток
        // копируется в хранилище с начала.
        content.Position = 0;
        return new(RecipeFileUploadOutcome.Ok, Content: content, Extension: extension);
    }
}
