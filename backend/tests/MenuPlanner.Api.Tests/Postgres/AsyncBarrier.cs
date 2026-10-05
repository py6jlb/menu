namespace MenuPlanner.Api.Tests.Postgres;

/// <summary>
/// Детерминированная точка рандеву для нескольких независимых операций
/// (запросов/контекстов). Операции встречаются на именованном шаге и
/// продолжают вместе, поэтому пересечение задаётся кодом, а не случайными
/// задержками. Число участников фиксировано; после каждого шага барьер
/// сбрасывается и пригоден для следующего.
/// </summary>
public sealed class AsyncBarrier
{
    private readonly int _participants;
    private readonly object _sync = new();
    private int _waiting;
    private TaskCompletionSource _release = CreateSource();

    public AsyncBarrier(int participants)
    {
        if (participants < 1)
            throw new ArgumentOutOfRangeException(nameof(participants), participants,
                "Число участников барьера должно быть положительным.");
        _participants = participants;
    }

    /// <summary>Приостановиться, пока все участники не достигнут этого шага.</summary>
    public async Task SignalAndWaitAsync(CancellationToken cancellationToken = default)
    {
        Task release;
        lock (_sync)
        {
            _waiting++;
            if (_waiting == _participants)
            {
                var completed = _release;
                _waiting = 0;
                _release = CreateSource();
                completed.TrySetResult();
                return;
            }

            release = _release.Task;
        }

        await release.WaitAsync(cancellationToken);
    }

    private static TaskCompletionSource CreateSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
