using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Time.Testing;

namespace Fulcrum.Tests;

public class RetryingHandlerTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly FakeHttpHandler _http = new();

    private Task<HttpResponseMessage> Send()
    {
        var invoker = new HttpMessageInvoker(new RetryingHandler(_time) { InnerHandler = _http });
        return invoker.SendAsync(new HttpRequestMessage(HttpMethod.Post, "https://example.com/") { Content = new StringContent("{}") }, TestContext.Current.CancellationToken);
    }

    private async Task AdvanceAndExpect(TimeSpan by, int requests)
    {
        _time.Advance(by);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_http.Requests.Count < requests && DateTime.UtcNow < deadline)
            await Task.Delay(5, TestContext.Current.CancellationToken);
        await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.Equal(requests, _http.Requests.Count);
    }

    private static void RetryAfter(HttpResponseMessage r, TimeSpan delta) => r.Headers.RetryAfter = new RetryConditionHeaderValue(delta);

    [Fact]
    public async Task Retries_429_three_times_then_returns_last_response()
    {
        _http.Respond(HttpStatusCode.TooManyRequests).Respond(HttpStatusCode.TooManyRequests).Respond(HttpStatusCode.TooManyRequests, "last");

        var send = Send();
        await AdvanceAndExpect(TimeSpan.FromMilliseconds(400), 2);
        await AdvanceAndExpect(TimeSpan.FromMilliseconds(600), 3);
        var response = await send;

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("last", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Retries_transient_5xx(HttpStatusCode status)
    {
        _http.Respond(status).Respond(HttpStatusCode.OK);

        var send = Send();
        await AdvanceAndExpect(TimeSpan.FromMilliseconds(400), 2);

        Assert.Equal(HttpStatusCode.OK, (await send).StatusCode);
    }

    [Fact]
    public async Task Honours_Retry_After_seconds()
    {
        _http.Respond(HttpStatusCode.TooManyRequests, configure: r => RetryAfter(r, TimeSpan.FromSeconds(5))).Respond(HttpStatusCode.OK);

        var send = Send();
        await AdvanceAndExpect(TimeSpan.FromSeconds(4.9), 1);
        await AdvanceAndExpect(TimeSpan.FromSeconds(0.1), 2);

        Assert.Equal(HttpStatusCode.OK, (await send).StatusCode);
    }

    [Fact]
    public async Task Honours_Retry_After_date()
    {
        var at = _time.GetUtcNow().AddSeconds(3);
        _http.Respond(HttpStatusCode.ServiceUnavailable, configure: r => r.Headers.RetryAfter = new RetryConditionHeaderValue(at)).Respond(HttpStatusCode.OK);

        var send = Send();
        await AdvanceAndExpect(TimeSpan.FromSeconds(2.9), 1);
        await AdvanceAndExpect(TimeSpan.FromSeconds(0.1), 2);

        Assert.Equal(HttpStatusCode.OK, (await send).StatusCode);
    }

    [Fact]
    public async Task Uses_exponential_backoff_without_Retry_After()
    {
        _http.Respond(HttpStatusCode.TooManyRequests).Respond(HttpStatusCode.TooManyRequests).Respond(HttpStatusCode.OK);

        var send = Send();
        await AdvanceAndExpect(TimeSpan.FromMilliseconds(199), 1);
        await AdvanceAndExpect(TimeSpan.FromMilliseconds(201), 2);
        await AdvanceAndExpect(TimeSpan.FromMilliseconds(399), 2);
        await AdvanceAndExpect(TimeSpan.FromMilliseconds(201), 3);

        Assert.Equal(HttpStatusCode.OK, (await send).StatusCode);
    }

    [Fact]
    public async Task Retries_HttpRequestException_then_succeeds()
    {
        _http.Throw(new HttpRequestException("reset")).Respond(HttpStatusCode.OK);

        var send = Send();
        await AdvanceAndExpect(TimeSpan.FromMilliseconds(400), 2);

        Assert.Equal(HttpStatusCode.OK, (await send).StatusCode);
    }

    [Fact]
    public async Task Does_not_retry_400()
    {
        _http.Respond(HttpStatusCode.BadRequest);

        var response = await Send();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(_http.Requests);
    }

    [Fact]
    public async Task Rethrows_after_final_HttpRequestException()
    {
        var last = new HttpRequestException("third");
        _http.Throw(new HttpRequestException("first")).Throw(new HttpRequestException("second")).Throw(last);

        var send = Send();
        await AdvanceAndExpect(TimeSpan.FromMilliseconds(400), 2);
        await AdvanceAndExpect(TimeSpan.FromMilliseconds(600), 3);

        Assert.Same(last, await Assert.ThrowsAsync<HttpRequestException>(() => send));
    }

    [Fact]
    public async Task Returns_the_response_when_Retry_After_exceeds_the_total_budget()
    {
        _http.Respond(HttpStatusCode.TooManyRequests, "slow down", r => RetryAfter(r, TimeSpan.FromSeconds(60)));

        var response = await Send();

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("slow down", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Single(_http.Requests);
    }
}
