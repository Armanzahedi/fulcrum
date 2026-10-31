using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fulcrum.OpenAI;

public sealed class OpenAIClient : ISystemOne
{
    private readonly HttpClient _http;
    private readonly OpenAIOptions _options;
    private readonly Uri _endpoint;

    public OpenAIClient(HttpClient http, OpenAIOptions options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new FulcrumException("OpenAI: no API key; set OpenAIOptions.ApiKey or the OPENAI_API_KEY environment variable");
        if (options.MaxAttempts < 1)
            throw new FulcrumException("OpenAI: MaxAttempts must be at least 1");
        _http = http;
        _options = options;
        _endpoint = Endpoint.Resolve("OpenAI", options.BaseAddress, "chat/completions");
    }

    public async Task<Answers> AskAsync(Content state, IReadOnlyList<Question> questions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(questions);
        var format = new ResponseFormat("json_schema", new JsonSchemaFormat("fulcrum_answers", true, SchemaBuilder.Build(questions)));
        var messages = new List<ChatMessage>
        {
            new("system", PromptBuilder.System(questions)),
            new("user", UserContent(state)),
        };

        for (var attempt = 1; ; attempt++)
        {
            var reply = await SendAsync(new ChatRequest(_options.Model, 0, messages, format), cancellationToken).ConfigureAwait(false);
            var (answers, violations) = Parse(reply, questions);
            if (violations.Count == 0)
                violations.AddRange(answers.Validate(state, questions));
            if (violations.Count == 0)
                return answers;
            if (attempt >= _options.MaxAttempts)
                throw new SystemOneException(violations);

            messages.Add(new ChatMessage("assistant", reply));
            messages.Add(new ChatMessage("user",
                $"Your previous answer had these problems:\n- {string.Join("\n- ", violations)}\nAnswer again following the schema exactly."));
        }
    }

    private static JsonArray UserContent(Content state)
    {
        var parts = new JsonArray();
        if (state.Text.Length > 0)
            parts.Add((JsonNode)new JsonObject { ["type"] = "text", ["text"] = state.Text });
        foreach (var image in state.Images)
        {
            var url = image.Bytes is { } bytes ? $"data:{image.MediaType};base64,{Convert.ToBase64String(bytes.Span)}" : image.Url!.ToString();
            parts.Add((JsonNode)new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = url } });
        }
        return parts;
    }

    private async Task<string> SendAsync(ChatRequest chat, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(chat, OpenAIJsonContext.Default.ChatRequest);
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        string body;
        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new SystemOneException($"OpenAI returned {(int)response.StatusCode}: {body[..Math.Min(body.Length, 500)]}");
        }
        catch (HttpRequestException ex)
        {
            throw new SystemOneException("OpenAI request failed", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SystemOneException("OpenAI request timed out", ex);
        }

        ChatResponse? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize(body, OpenAIJsonContext.Default.ChatResponse);
        }
        catch (JsonException ex)
        {
            throw new SystemOneException($"OpenAI returned an unreadable body: {ex.Message}");
        }
        var message = envelope?.Choices is [{ Message: { } m }, ..] ? m : throw new SystemOneException("OpenAI returned an unreadable body: no choices");
        return message.Content ?? throw new SystemOneException($"OpenAI returned no answer: {message.Refusal ?? "empty content"}");
    }

    private static (Answers Answers, List<string> Violations) Parse(string reply, IReadOnlyList<Question> questions)
    {
        var violations = new List<string>();
        var raw = new Dictionary<string, Answer>();
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(reply) as JsonObject;
        }
        catch (JsonException)
        {
            root = null;
        }
        if (root is null)
        {
            violations.Add("the reply is not a JSON object");
            return (new Answers(raw), violations);
        }

        foreach (var q in questions)
        {
            try
            {
                if (root[q.Name] is JsonObject node)
                    raw[q.Name] = ToAnswer(q, node);
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
            {
                violations.Add($"{q.Name}: malformed answer ({ex.Message})");
            }
        }
        return (new Answers(raw), violations);
    }

    private static Answer ToAnswer(Question q, JsonObject node) => q.Kind switch
    {
        QuestionKind.Choice => new ChoiceAnswer(
            q.Name,
            Required(node, "choice").GetValue<string>(),
            Required(node, "probabilities").AsObject().ToDictionary(p => p.Key, p => Value(p.Value, p.Key).GetValue<double>())),
        QuestionKind.YesNo => new YesNoAnswer(q.Name, Required(node, "probability").GetValue<double>()),
        QuestionKind.Score => new ScoreAnswer(
            q.Name,
            Required(node, "level").GetValue<int>(),
            [.. Required(node, "probabilities").AsArray().Select(p => Value(p, "probabilities").GetValue<double>())]),
        _ => new BoxAnswer(q.Name, [.. Required(node, "boxes").AsArray().Select(b => Value(b, "boxes")).Select(b => new Box(
            Required(b, "image").GetValue<int>(),
            Required(b, "x").GetValue<double>(),
            Required(b, "y").GetValue<double>(),
            Required(b, "w").GetValue<double>(),
            Required(b, "h").GetValue<double>(),
            Required(b, "confidence").GetValue<double>()))]),
    };

    private static JsonNode Required(JsonNode node, string property) => Value(node[property], property);

    private static JsonNode Value(JsonNode? node, string property) => node ?? throw new InvalidOperationException($"'{property}' is missing");
}
