using System.Net;
using System.Text.Json.Nodes;
using Fulcrum.OpenAI;
using static Fulcrum.Tests.Triage;
using static Fulcrum.Tests.IdCheck;

namespace Fulcrum.Tests;

public class OpenAIClientTests
{
    private readonly FakeHttpHandler _http = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private OpenAIClient Client(int maxAttempts = 3) =>
        new(new HttpClient(_http), new OpenAIOptions { ApiKey = "secret", Model = "gpt-test", MaxAttempts = maxAttempts });

    private static string Reply(string content) =>
        new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content } }) }.ToJsonString();

    private const string Valid = """
        {"team":{"choice":"Billing","probabilities":{"Billing":0.9,"Technical":0.07,"Sales":0.03}},
         "safe_to_automate":{"probability":0.95},
         "urgency":{"level":0,"probabilities":[0.8,0.15,0.05]}}
        """;

    private const string UndeclaredChoice = """
        {"team":{"choice":"Legal","probabilities":{"Billing":0.9,"Technical":0.07,"Sales":0.03}},
         "safe_to_automate":{"probability":0.95},
         "urgency":{"level":0,"probabilities":[0.8,0.15,0.05]}}
        """;

    private static JsonNode Body(CapturedRequest request) => JsonNode.Parse(request.Body!)!;

    [Fact]
    public async Task Sends_system_prompt_user_text_and_images_with_strict_schema()
    {
        _http.Respond(HttpStatusCode.OK, Reply("""{"face":{"boxes":[]}}"""));

        await Client().AskAsync(Content.From("doc").WithImage(new byte[] { 1, 2, 3 }, "image/png"), [FaceQ], Ct);

        var request = Assert.Single(_http.Requests);
        Assert.Equal(new Uri("https://api.openai.com/v1/chat/completions"), request.Message.RequestUri);
        Assert.Equal("secret", request.Message.Headers.Authorization?.Parameter);
        var body = Body(request);
        Assert.Equal("gpt-test", body["model"]!.GetValue<string>());
        Assert.Equal(0, body["temperature"]!.GetValue<double>());
        var messages = body["messages"]!.AsArray();
        Assert.Equal("system", messages[0]!["role"]!.GetValue<string>());
        Assert.Contains("face", messages[0]!["content"]!.GetValue<string>());
        Assert.Contains("origin top-left", messages[0]!["content"]!.GetValue<string>());
        var parts = messages[1]!["content"]!.AsArray();
        Assert.Equal("user", messages[1]!["role"]!.GetValue<string>());
        Assert.Equal(2, parts.Count);
        Assert.Equal("text", parts[0]!["type"]!.GetValue<string>());
        Assert.Equal("doc", parts[0]!["text"]!.GetValue<string>());
        Assert.Equal("image_url", parts[1]!["type"]!.GetValue<string>());
        Assert.Equal("data:image/png;base64,AQID", parts[1]!["image_url"]!["url"]!.GetValue<string>());
        var format = body["response_format"]!;
        Assert.Equal("json_schema", format["type"]!.GetValue<string>());
        Assert.Equal("fulcrum_answers", format["json_schema"]!["name"]!.GetValue<string>());
        Assert.True(format["json_schema"]!["strict"]!.GetValue<bool>());
        Assert.Equal("face", format["json_schema"]!["schema"]!["required"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task Sends_url_images_as_is()
    {
        _http.Respond(HttpStatusCode.OK, Reply("""{"face":{"boxes":[]}}"""));

        await Client().AskAsync(Content.Image(new Uri("https://example.com/a.png")), [FaceQ], Ct);

        var parts = Body(_http.Requests.Single())["messages"]![1]!["content"]!.AsArray();
        Assert.Equal("https://example.com/a.png", Assert.Single(parts)!["image_url"]!["url"]!.GetValue<string>());
    }

    [Fact]
    public async Task Parses_valid_reply_into_raw_answers()
    {
        _http.Respond(HttpStatusCode.OK, Reply(Valid));

        var answers = await Client().AskAsync("t", [TeamQ, SafeQ, UrgencyQ], Ct);

        Assert.Equal(Team.Billing, answers.Get(TeamQ).Value);
        Assert.Equal(0.95, answers.Get(SafeQ).Probability);
        Assert.Equal(Urgency.CanWait, answers.Get(UrgencyQ).Value);
        Assert.Equal(0.15, answers.Get(UrgencyQ).Probabilities[Urgency.ThisWeek]);
        Assert.Single(_http.Requests);
    }

    [Fact]
    public async Task Parses_boxes()
    {
        _http.Respond(HttpStatusCode.OK, Reply("""{"face":{"boxes":[{"image":0,"x":0.62,"y":0.18,"w":0.21,"h":0.3,"confidence":0.97}]}}"""));

        var answers = await Client().AskAsync(Content.Image(new Uri("https://example.com/a.png")), [FaceQ], Ct);

        Assert.Equal(new Box(0, 0.62, 0.18, 0.21, 0.3, 0.97), Assert.Single(answers.Get(FaceQ).Boxes));
    }

    [Fact]
    public async Task Reasks_with_violations_then_succeeds()
    {
        _http.Respond(HttpStatusCode.OK, Reply(UndeclaredChoice)).Respond(HttpStatusCode.OK, Reply(Valid));

        var answers = await Client().AskAsync("t", [TeamQ, SafeQ, UrgencyQ], Ct);

        Assert.Equal(Team.Billing, answers.Get(TeamQ).Value);
        Assert.Equal(2, _http.Requests.Count);
        var messages = Body(_http.Requests[1])["messages"]!.AsArray();
        Assert.Equal(4, messages.Count);
        Assert.Equal("assistant", messages[2]!["role"]!.GetValue<string>());
        Assert.Equal(UndeclaredChoice, messages[2]!["content"]!.GetValue<string>());
        Assert.Equal("user", messages[3]!["role"]!.GetValue<string>());
        Assert.Contains("team: 'Legal' is not a declared option", messages[3]!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task Reasks_when_reply_content_is_not_json()
    {
        _http.Respond(HttpStatusCode.OK, Reply("I think it is billing")).Respond(HttpStatusCode.OK, Reply(Valid));

        await Client().AskAsync("t", [TeamQ, SafeQ, UrgencyQ], Ct);

        Assert.Equal(2, _http.Requests.Count);
    }

    [Fact]
    public async Task Reasks_when_an_answer_is_missing_a_field()
    {
        _http.Respond(HttpStatusCode.OK, Reply("""{"safe_to_automate":{}}""")).Respond(HttpStatusCode.OK, Reply("""{"safe_to_automate":{"probability":0.4}}"""));

        var answers = await Client().AskAsync("t", [SafeQ], Ct);

        Assert.Equal(0.4, answers.Get(SafeQ).Probability);
        var feedback = Body(_http.Requests[1])["messages"]![3]!["content"]!.GetValue<string>();
        Assert.Contains("safe_to_automate: malformed answer ('probability' is missing)", feedback);
        Assert.DoesNotContain("not answered", feedback);
    }

    [Fact]
    public async Task Reasks_when_reply_has_duplicate_keys()
    {
        _http.Respond(HttpStatusCode.OK, Reply("""{"team":{"choice":"Billing","probabilities":{"Billing":0.5,"Billing":0.5,"Technical":0,"Sales":0}}}"""))
            .Respond(HttpStatusCode.OK, Reply("""{"team":{"choice":"Billing","probabilities":{"Billing":0.9,"Technical":0.05,"Sales":0.05}}}"""));

        var answers = await Client().AskAsync("t", [TeamQ], Ct);

        Assert.Equal(Team.Billing, answers.Get(TeamQ).Value);
        Assert.Equal(2, _http.Requests.Count);
    }

    [Fact]
    public async Task Fails_after_MaxAttempts_with_SystemOneException_listing_violations()
    {
        _http.Respond(HttpStatusCode.OK, Reply(UndeclaredChoice)).Respond(HttpStatusCode.OK, Reply(UndeclaredChoice));

        var ex = await Assert.ThrowsAsync<SystemOneException>(() => Client(maxAttempts: 2).AskAsync("t", [TeamQ, SafeQ, UrgencyQ], Ct));

        Assert.Equal(2, _http.Requests.Count);
        Assert.Contains("team: 'Legal' is not a declared option", ex.Violations);
    }

    [Fact]
    public async Task Non_2xx_throws_SystemOneException()
    {
        _http.Respond(HttpStatusCode.BadRequest, """{"error":"model does not support images"}""");

        var ex = await Assert.ThrowsAsync<SystemOneException>(() => Client().AskAsync("t", [SafeQ], Ct));

        Assert.Contains("OpenAI returned 400", ex.Message);
        Assert.Contains("does not support images", ex.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"choices":[]}""")]
    [InlineData("""{"choices":[{"message":{"role":"assistant","content":null,"refusal":"no"}}]}""")]
    public async Task Unreadable_envelope_throws_SystemOneException(string body)
    {
        _http.Respond(HttpStatusCode.OK, body);

        var ex = await Assert.ThrowsAsync<SystemOneException>(() => Client().AskAsync("t", [SafeQ], Ct));

        Assert.StartsWith("OpenAI", ex.Message);
        Assert.Single(_http.Requests);
    }

    [Fact]
    public async Task Base_address_without_trailing_slash_keeps_its_path()
    {
        _http.Respond(HttpStatusCode.OK, Reply("""{"safe_to_automate":{"probability":0.5}}"""));
        var client = new OpenAIClient(new HttpClient(_http), new OpenAIOptions { ApiKey = "k", BaseAddress = new Uri("https://api.groq.com/openai/v1") });

        await client.AskAsync("t", [SafeQ], Ct);

        Assert.Equal(new Uri("https://api.groq.com/openai/v1/chat/completions"), _http.Requests.Single().Message.RequestUri);
    }
}
