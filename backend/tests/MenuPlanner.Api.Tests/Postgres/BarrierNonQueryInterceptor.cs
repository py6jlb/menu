using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MenuPlanner.Api.Tests.Postgres;

/// <summary>
/// Детерминированная точка рандеву на команде блокировки строки пользователя.
/// Каждый контекст получает свой экземпляр; первая подходящая команда
/// приостанавливается, пока все участники не дойдут до неё, — так пересечение
/// задаётся кодом, а не задержками.
/// </summary>
internal sealed class BarrierNonQueryInterceptor : DbCommandInterceptor
{
    private readonly AsyncBarrier _barrier;
    private readonly string _fragment;
    private int _entered;

    public BarrierNonQueryInterceptor(AsyncBarrier barrier, string fragment = "FOR UPDATE")
    {
        _barrier = barrier;
        _fragment = fragment;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains(_fragment, StringComparison.Ordinal) &&
            Interlocked.Exchange(ref _entered, 1) == 0)
        {
            await _barrier.SignalAndWaitAsync(cancellationToken);
        }

        return result;
    }
}
