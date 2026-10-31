using System.Net;
using Fulcrum.Jev;
using static Fulcrum.Tests.Triage;
using static Fulcrum.Tests.IdCheck;

namespace Fulcrum.Tests;

public class JevClientTests
{
    private readonly FakeHttpHandler _http = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private JevClient Client() => new(new HttpClient(_http), new JevOptions { ApiKey = "secret", Model = "jev-test" });

    private const string AllKinds = """
        {"answers":{
          "team":{"type":"choice","choice":"Billing","confidence":0.1,"probabilities":{"Billing":0.9,"Technical":0.07,"Sales":0.03}},
          "safe_to_automate":{"type":"noul","probability":0.95},
          "urgency":{"type":"score","level":2,"confidence":0.2,"probabilities":[0.1,0.2,0.7]},
          "face":{"type":"box","boxes":[{"image":0,"x":0.62,"y":0.18,"w":0.21,"h":0.3,"confidence":0.97}]}
        },"usage":{"tokens":5}}
        """;

    [Fact]
    public async Task Sends_bearer_auth_model_state_and_questions()
    {
        _http.Respond(HttpStatusCode.OK, AllKinds);

        await Client().AskAsync("Subject: Refund\n\nPlease refund order 123", [UrgencyQ, TeamQ, SafeQ], Ct);

        var request = Assert.Single(_http.Requests);
        Assert.Equal(HttpMethod.Post, request.Message.Method);
        Assert.Equal(new Uri("https://api.typesafe.ai/v1/systemone"), request.Message.RequestUri);
        Assert.Equal("Bearer", request.Message.Headers.Authorization?.Scheme);
        Assert.Equal("secret", request.Message.Headers.Authorization?.Parameter);
        Assert.Equal("application/json", request.Message.Content?.Headers.ContentType?.MediaType);
        Assert.Equal(
            """{"model":"jev-test","state":{"text":"Subject: Refund\n\nPlease refund order 123"},"questions":[{"name":"urgency","type":"score","instruction":"How urgently does this need a human?","levels":["Can wait a week","Needs attention this week","Needs attention today"]},{"name":"team","type":"choice","instruction":"Which team should own this ticket?","options":[{"name":"Billing","description":"Payments, refunds, invoices, subscriptions"},{"name":"Technical","description":"Bugs, errors, integrations, outages"},{"name":"Sales","description":"Pricing, plans, new accounts"}]},{"name":"safe_to_automate","type":"noul","instruction":"Can a standard reply be sent without a human reading it?"}]}""",
            request.Body);
    }

    [Fact]
    public async Task Sends_images_as_base64_or_url()
    {
        _http.Respond(HttpStatusCode.OK, AllKinds);
        var content = Content.From("doc").WithImage(new byte[] { 1, 2, 3 }, "image/png").WithImage(new Uri("https://example.com/b.jpg"));

        await Client().AskAsync(content, [FaceQ], Ct);

        Assert.Contains(
            "\"state\":{\"text\":\"doc\",\"images\":[{\"data\":\"AQID\",\"media_type\":\"image/png\"},{\"url\":\"https://example.com/b.jpg\"}]}",
            _http.Requests.Single().Body);
        Assert.Contains("\"questions\":[{\"name\":\"face\",\"type\":\"box\",\"instruction\":", _http.Requests.Single().Body);
    }

    [Fact]
    public async Task Omits_images_when_none()
    {
        _http.Respond(HttpStatusCode.OK, AllKinds);

        await Client().AskAsync("text", [SafeQ], Ct);

        Assert.DoesNotContain("images", _http.Requests.Single().Body);
    }

    [Fact]
    public async Task Maps_every_answer_kind()
    {
        _http.Respond(HttpStatusCode.OK, AllKinds);

        var answers = await Client().AskAsync(Content.From("t").WithImage(new byte[] { 1 }, "image/png"), [TeamQ, SafeQ, UrgencyQ, FaceQ], Ct);

        var team = Assert.IsType<ChoiceAnswer>(answers.Raw["team"]);
        Assert.Equal("Billing", team.Choice);
        Assert.Equal(0.9, team.Confidence);
        Assert.Equal(0.03, team.Probabilities["Sales"]);
        Assert.Equal(0.95, Assert.IsType<YesNoAnswer>(answers.Raw["safe_to_automate"]).Probability);
        var urgency = Assert.IsType<ScoreAnswer>(answers.Raw["urgency"]);
        Assert.Equal(2, urgency.Level);
        Assert.Equal([0.1, 0.2, 0.7], urgency.Probabilities);
        Assert.Equal(0.7, urgency.Confidence);
        Assert.Equal(new Box(0, 0.62, 0.18, 0.21, 0.3, 0.97), Assert.Single(Assert.IsType<BoxAnswer>(answers.Raw["face"]).Boxes));
    }

    [Fact]
    public async Task Non_2xx_throws_SystemOneException_with_status_and_body_prefix()
    {
        var body = new string('a', 500) + new string('b', 100);
        _http.Respond(HttpStatusCode.Unauthorized, body);

        var ex = await Assert.ThrowsAsync<SystemOneException>(() => Client().AskAsync("t", [SafeQ], Ct));

        Assert.Contains("401", ex.Message);
        Assert.Contains(new string('a', 500), ex.Message);
        Assert.DoesNotContain("b", ex.Message.Replace("Jev returned", ""));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"answers":{"safe_to_automate":{"type":"mystery"}}}""")]
    [InlineData("""{"answers":{"team":{"type":"choice","probabilities":{"Billing":1}}}}""")]
    [InlineData("""{"answers":{"safe_to_automate":null}}""")]
    [InlineData("""{"answers":{"safe_to_automate":{"type":"noul","probability":0.4},"safe_to_automate":{"type":"noul","probability":0.6}}}""")]
    [InlineData("""{"answers":{"team":{"type":"choice","choice":"Billing","probabilities":{"Billing":0.5,"Billing":0.5}}}}""")]
    [InlineData("""{"answers":{"team":{"type":"choice","choice":"Billing","probabilities":{"Billing":"high"}}}}""")]
    [InlineData("""{"answers":{"face":{"type":"box","boxes":[{"image":0,"x":0.1,"y":0.1,"confidence":0.9}]}}}""")]
    public async Task Unreadable_body_throws_SystemOneException(string body)
    {
        _http.Respond(HttpStatusCode.OK, body);

        var ex = await Assert.ThrowsAsync<SystemOneException>(() => Client().AskAsync("t", [SafeQ], Ct));

        Assert.Contains("unreadable", ex.Message);
    }

    [Fact]
    public async Task HttpRequestException_wraps_as_SystemOneException()
    {
        var inner = new HttpRequestException("connection refused");
        _http.Throw(inner);

        var ex = await Assert.ThrowsAsync<SystemOneException>(() => Client().AskAsync("t", [SafeQ], Ct));

        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public async Task Timeout_wraps_as_SystemOneException_but_caller_cancellation_propagates()
    {
        _http.Throw(new TaskCanceledException("timed out"));
        await Assert.ThrowsAsync<SystemOneException>(() => Client().AskAsync("t", [SafeQ], Ct));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _http.Throw(new TaskCanceledException("cancelled", null, cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client().AskAsync("t", [SafeQ], cts.Token));
    }

    [Fact]
    public async Task Base_address_without_trailing_slash_keeps_its_path()
    {
        _http.Respond(HttpStatusCode.OK, AllKinds);
        var client = new JevClient(new HttpClient(_http), new JevOptions { ApiKey = "k", BaseAddress = new Uri("https://jev.example.com/api") });

        await client.AskAsync("t", [SafeQ], Ct);

        Assert.Equal(new Uri("https://jev.example.com/api/v1/systemone"), _http.Requests.Single().Message.RequestUri);
    }
}
