using Microsoft.Extensions.Logging;

namespace MenuPlanner.Api.Recipes.Documents;

/// <summary>
/// Результат изменения БД, согласуемого с файлом документа: <see cref="Committed"/> = false
/// означает, что БД не изменилась (например, конфликт ревизии) и новый файл нужно
/// убрать компенсацией, а предыдущий не трогать.
/// </summary>
public sealed record DocumentCommit<T>(bool Committed, T Value);

/// <summary>
/// Предметный модуль жизненного цикла PDF-документа рецепта. Согласует файл с commit
/// БД: новый файл появляется до изменения БД, при неуспехе убирается компенсацией,
/// предыдущий файл удаляется только после успешного commit. Ошибки компенсации и
/// уборки не возвращают ложный общий неуспех уже завершённого изменения: файл
/// остаётся диагностируемым кандидатом для <see cref="DocumentGarbageCollector"/>.
/// Scoped-сервис, держит <see cref="IDocumentStore"/>.
/// </summary>
public sealed class DocumentLifecycle
{
    private readonly IDocumentStore _store;
    private readonly ILogger<DocumentLifecycle> _logger;

    public DocumentLifecycle(IDocumentStore store, ILogger<DocumentLifecycle> logger)
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
        Guid recipeId,
        string extension,
        Stream content,
        string? previous,
        Func<string, Task<DocumentCommit<T>>> commitAsync,
        CancellationToken cancellationToken = default)
    {
        var staged = await _store.SaveAsync(recipeId, extension, content, cancellationToken);
        return await CommitOrDiscardAsync(staged, previous, () => commitAsync(staged));
    }

    /// <summary>
    /// Скопировать файл источника в новый, выполнить изменение БД и согласовать
    /// результат так же, как в <see cref="ReplaceAsync{T}"/>. Null-копия (источник
    /// уже заменён) — не ошибка: изменение выполняется без документа.
    /// </summary>
    public async Task<T> CopyFromAsync<T>(
        Guid recipeId,
        string? sourceName,
        string? previous,
        Func<string?, Task<DocumentCommit<T>>> commitAsync,
        CancellationToken cancellationToken = default)
    {
        var copied = await _store.CopyAsync(recipeId, sourceName, cancellationToken);
        return await CommitOrDiscardAsync(copied, previous, () => commitAsync(copied));
    }

    /// <summary>
    /// Убрать предыдущий файл после уже подтверждённого commit. Повторяемая и
    /// безопасная: ошибка удаления не делает завершённое изменение неуспешным,
    /// файл подберёт уборка.
    /// </summary>
    public void Retire(string? previousName) =>
        RemoveSafely(previousName,
            "Документ: не удалось удалить предыдущий файл {File} после commit; оставлен кандидатом уборки.");

    /// <summary>
    /// Компенсация: убрать новый файл после неуспешной записи БД. Повторяемая и
    /// безопасная: если удаление само не удалось, файл остаётся кандидатом для
    /// уборки, но исходная ошибка БД не маскируется.
    /// </summary>
    public void Discard(string? stagedName) =>
        RemoveSafely(stagedName,
            "Документ: не удалось удалить несохранённый файл {File}; оставлен кандидатом уборки.");

    /// <summary>
    /// Скопировать файл источника в новый <em>до</em> изменения БД для путей с
    /// явной транзакцией, где commit отделён от подготовки. Null, если источника
    /// уже нет (конкурентная замена) — не ошибка. Компенсацию и уборку вызывающий
    /// делает явно через <see cref="Discard"/> и <see cref="Retire"/>.
    /// </summary>
    public Task<string?> StageCopyAsync(
        Guid recipeId, string? sourceName, CancellationToken cancellationToken = default) =>
        _store.CopyAsync(recipeId, sourceName, cancellationToken);

    private async Task<T> CommitOrDiscardAsync<T>(
        string? created, string? previous, Func<Task<DocumentCommit<T>>> commitAsync)
    {
        DocumentCommit<T> commit;
        try
        {
            commit = await commitAsync();
        }
        catch
        {
            Discard(created);
            throw;
        }

        if (!commit.Committed)
        {
            Discard(created);
            return commit.Value;
        }

        Retire(previous);
        return commit.Value;
    }

    private void RemoveSafely(string? name, string failureMessage)
    {
        if (string.IsNullOrEmpty(name)) return;
        try
        {
            _store.Delete(name);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, failureMessage, name);
        }
    }
}
