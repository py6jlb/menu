using System.Globalization;

namespace MenuPlanner.Api.Auth;

/// <summary>
/// Единый порядок проверки лимитов для аутентификационных операций: сначала
/// ограниченный по частоте IP, затем операция/пользователь. Если IP уже
/// исчерпан, ключ операции не создаётся — поток уникальных email с одного
/// ограниченного адреса не растит store.
/// </summary>
public static class AuthRateLimitPolicy
{
    public static IResult? Check(
        FixedWindowRateLimiter limiter,
        string scope,
        string ip,
        int perIp,
        int perIdentity,
        string? identity,
        TimeSpan window,
        DateTime now)
    {
        var ipDecision = limiter.TryConsume($"{scope}:ip:{ip}", perIp, window, now);
        if (!ipDecision.Allowed)
            return RateLimitResults.TooManyRequests(ipDecision.RetryAfter);

        if (identity is not null)
        {
            var identityDecision = limiter.TryConsume($"{scope}:id:{identity}", perIdentity, window, now);
            if (!identityDecision.Allowed)
                return RateLimitResults.TooManyRequests(identityDecision.RetryAfter);
        }

        return null;
    }
}

/// <summary>Согласованный отказ лимита: <c>429</c> с заголовком <c>Retry-After</c>.</summary>
public static class RateLimitResults
{
    public static IResult TooManyRequests(TimeSpan retryAfter) =>
        new RateLimitExceededResult(Seconds(retryAfter), message: null);

    public static IResult TooManyRequests(int retryAfterSeconds, string? message = null) =>
        new RateLimitExceededResult(Math.Max(1, retryAfterSeconds), message);

    private static int Seconds(TimeSpan value) =>
        Math.Max(1, (int)Math.Ceiling(value.TotalSeconds));

    private sealed class RateLimitExceededResult : IResult
    {
        private readonly int _seconds;
        private readonly string? _message;

        public RateLimitExceededResult(int seconds, string? message)
        {
            _seconds = seconds;
            _message = message;
        }

        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            httpContext.Response.Headers.RetryAfter = _seconds.ToString(CultureInfo.InvariantCulture);
            var message = _message ?? $"Слишком много запросов. Повторите через {_seconds} сек.";
            return httpContext.Response.WriteAsJsonAsync(new ErrorDto(message));
        }
    }
}
