using System.Net;
using System.Net.Http.Headers;

namespace Fulcrum;

internal sealed class RetryingHandler(TimeProvider time) : DelegatingHandler
{
    internal const int MaxAttempts = 3;
    internal static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(200);
    internal static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(30);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TotalTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var ct = linked.Token;
        var deadline = time.GetUtcNow() + TotalTimeout;

        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await base.SendAsync(request, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < MaxAttempts)
            {
                await Task.Delay(Delay(attempt, null), time, ct).ConfigureAwait(false);
                continue;
            }

            if (attempt == MaxAttempts || !IsTransient(response.StatusCode))
                return response;

            var delay = Delay(attempt, response.Headers.RetryAfter);
            if (time.GetUtcNow() + delay > deadline)
                return response;
            response.Dispose();
            await Task.Delay(delay, time, ct).ConfigureAwait(false);
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    private TimeSpan Delay(int retry, RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is { } delta)
            return delta;
        if (retryAfter?.Date is { } date)
            return date > time.GetUtcNow() ? date - time.GetUtcNow() : TimeSpan.Zero;
        return BaseDelay * Math.Pow(2, retry - 1) + BaseDelay * Random.Shared.NextDouble();
    }
}
