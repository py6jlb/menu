using Microsoft.Extensions.Logging;

namespace MenuPlanner.Api.Recipes.Files;

/// <summary>
/// Результат изменения БД, согласуемого с файлом рецепта: <see cref="Committed"/> = false
/// означает, что БД не изменилась (например, конфликт ревизии) и новый файл нужно
/// убрать компенсацией, а предыдущий не трогать.
/// </summary>
public sealed record RecipeFileCommit<T>(bool Committed, T Value);

/// <summary>
/// Предметный модуль жизненного цикла файла рецепта. Согласует файл с commit БД:
/// новый файл появляется до изменения БД, при неуспехе убирается компенсацией,
/// предыдущий файл удаляется только после успешного commit. Ошибки компенсации и
/// уборки не возвращают ложный общий неуспех уже завершённого изменения: файл
/// остаётся диагностируемым кандидатом для <see cref="RecipeGarbageCollector"/>.
/// Вид задаёт каталог и лог-префикс; поведение для фото и документа одно.
/// Scoped-сервис, держит <see cref="IRecipeFileStore"/>.
/// </summary>
public sealed class RecipeFileLifecycle
{
    private readonly IRecipeFileStore _store;
    private readonly ILogger<RecipeFileLifecycle> _logger;

    public RecipeFileLifecycle(IRecipeFileStore store, ILogger<RecipeFileLifecycle> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Записать новый файл, выполнить изменение БД и согласовать результат:
    /// при подтверждённом commit удаляется предыдущий файл, при отказе БД новый
    /// файл убирается компенсацией, а предыдущий не трогается.
    /// </summary>
    public async Task<T> ReplaceAsync<T>(
        RecipeFileKind kind,
        Guid recipeId,
        string extension,
        Stream content,
        string? previous,
        Func<string, Task<RecipeFileCommit<T>>> commitAsync,
        CancellationToken cancellationToken = default)
    {
        var staged = await _store.SaveAsync(kind, recipeId, extension, content, cancellationToken);
        return await CommitOrDiscardAsync(kind, staged, previous, () => commitAsync(staged));
    }

    /// <summary>
    /// Скопировать файл источника в новый, выполнить изменение БД и согласовать
    /// результат так же, как в <see cref="ReplaceAsync{T}"/>. Null-копия (источник
    /// уже заменён) — не ошибка: изменение выполняется без файла.
    /// </summary>
    public async Task<T> CopyFromAsync<T>(
        RecipeFileKind kind,
        Guid recipeId,
        string? sourceName,
        string? previous,
        Func<string?, Task<RecipeFileCommit<T>>> commitAsync,
        CancellationToken cancellationToken = default)
    {
        var copied = await _store.CopyAsync(kind, recipeId, sourceName, cancellationToken);
        return await CommitOrDiscardAsync(kind, copied, previous, () => commitAsync(copied));
    }

    /// <summary>
    /// Убрать предыдущий файл после уже подтверждённого commit. Повторяемая и
    /// безопасная: ошибка удаления не делает завершённое изменение неуспешным,
    /// файл подберёт уборка.
    /// </summary>
    public void Retire(RecipeFileKind kind, string? previousName) =>
        RemoveSafely(kind, previousName,
            "не удалось удалить предыдущий файл {File} после commit; оставлен кандидатом уборки.");

    /// <summary>
    /// Компенсация: убрать новый файл после неуспешной записи БД. Повторяемая и
    /// безопасная: если удаление само не удалось, файл остаётся кандидатом для
    /// уборки, но исходная ошибка БД не маскируется.
    /// </summary>
    public void Discard(RecipeFileKind kind, string? stagedName) =>
        RemoveSafely(kind, stagedName,
            "не удалось удалить несохранённый файл {File}; оставлен кандидатом уборки.");

    /// <summary>
    /// Скопировать файл источника в новый <em>до</em> изменения БД для путей с
    /// явной транзакцией, где commit отделён от подготовки. Null, если источника
    /// уже нет (конкурентная замена) — не ошибка. Компенсацию и уборку вызывающий
    /// делает явно через <see cref="Discard"/> и <see cref="Retire"/>.
    /// </summary>
    public Task<string?> StageCopyAsync(
        RecipeFileKind kind, Guid recipeId, string? sourceName, CancellationToken cancellationToken = default) =>
        _store.CopyAsync(kind, recipeId, sourceName, cancellationToken);

    private async Task<T> CommitOrDiscardAsync<T>(
        RecipeFileKind kind, string? created, string? previous, Func<Task<RecipeFileCommit<T>>> commitAsync)
    {
        RecipeFileCommit<T> commit;
        try
        {
            commit = await commitAsync();
        }
        catch
        {
            // Неуспех изменения БД: новый файл не остаётся сиротой (или остаётся
            // кандидатом уборки, если компенсация сама не удалась).
            Discard(kind, created);
            throw;
        }

        if (!commit.Committed)
        {
            Discard(kind, created);
            return commit.Value;
        }

        Retire(kind, previous);
        return commit.Value;
    }

    private void RemoveSafely(RecipeFileKind kind, string? name, string failureMessage)
    {
        if (string.IsNullOrEmpty(name)) return;
        try
        {
            _store.Delete(kind, name);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception, RecipeFileCatalog.For(kind).LogPrefix + " " + failureMessage, name);
        }
    }
}
