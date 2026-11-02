using System.Net;

namespace Fulcrum.Tests;

internal sealed record CapturedRequest(HttpRequestMessage Message, string? Body);

internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Lock _lock = new();
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
    private readonly List<CapturedRequest> _requests = [];

    public IReadOnlyList<CapturedRequest> Requests
    {
        get
        {
            lock (_lock)
                return [.. _requests];
        }
    }

    public FakeHttpHandler Respond(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        lock (_lock)
            _responses.Enqueue(respond);
        return this;
    }

    public FakeHttpHandler Respond(HttpStatusCode status, string body = "", Action<HttpResponseMessage>? configure = null) => Respond(_ =>
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
        configure?.Invoke(response);
        return response;
    });

    public FakeHttpHandler Throw(Exception exception) => Respond(_ => throw exception);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Func<HttpRequestMessage, HttpResponseMessage> respond;
        lock (_lock)
        {
            _requests.Add(new CapturedRequest(request, body));
            if (!_responses.TryDequeue(out respond!))
                throw new InvalidOperationException($"FakeHttpHandler has no response scripted for request {_requests.Count}.");
        }
        return respond(request);
    }
}
