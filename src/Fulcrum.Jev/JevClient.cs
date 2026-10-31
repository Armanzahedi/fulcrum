using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Fulcrum.Jev;

[Experimental("FUL0001")]
public sealed class JevClient : ISystemOne
{
    private readonly HttpClient _http;
    private readonly JevOptions _options;
    private readonly Uri _endpoint;

    public JevClient(HttpClient http, JevOptions options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new FulcrumException("Jev: no API key; set JevOptions.ApiKey or the JEV_API_KEY environment variable");
        _http = http;
        _options = options;
        _endpoint = Endpoint.Resolve("Jev", options.BaseAddress, "v1/systemone");
    }

    public async Task<Answers> AskAsync(Content state, IReadOnlyList<Question> questions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(questions);
        var json = JsonSerializer.Serialize(BuildRequest(state, questions), JevJsonContext.Default.JevRequest);
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
                throw new SystemOneException($"Jev returned {(int)response.StatusCode}: {body[..Math.Min(body.Length, 500)]}");
        }
        catch (HttpRequestException ex)
        {
            throw new SystemOneException("Jev request failed", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SystemOneException("Jev request timed out", ex);
        }

        return Parse(body);
    }

    private JevRequest BuildRequest(Content state, IReadOnlyList<Question> questions) => new(
        _options.Model,
        new JevState(state.Text, state.Images.Count == 0 ? null : [.. state.Images.Select(ToWire)]),
        [.. questions.Select(ToWire)]);

    private static JevImage ToWire(Image image) => image.Bytes is { } bytes
        ? new JevImage(Convert.ToBase64String(bytes.Span), image.MediaType, null)
        : new JevImage(null, null, image.Url!.ToString());

    private static JevQuestion ToWire(Question q) => q.Kind switch
    {
        QuestionKind.Choice => new JevQuestion(q.Name, "choice", q.Text, [.. q.Choices.Select(c => new JevOption(c.Name, c.Description))], null),
        QuestionKind.Score => new JevQuestion(q.Name, "score", q.Text, null, [.. q.Choices.Select(c => c.Description)]),
        QuestionKind.YesNo => new JevQuestion(q.Name, "noul", q.Text, null, null),
        _ => new JevQuestion(q.Name, "box", q.Text, null, null),
    };

    private static Answers Parse(string body)
    {
        try
        {
            var response = JsonSerializer.Deserialize(body, JevJsonContext.Default.JevResponse);
            if (response?.Answers is not { } answers)
                throw Unreadable("no answers");
            return new Answers(answers.ToDictionary(a => a.Key, a => ToAnswer(a.Key, a.Value ?? throw Unreadable($"'{a.Key}' is null"))));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            throw Unreadable(ex.Message);
        }
    }

    private static Answer ToAnswer(string name, JevAnswer a) => a.Type switch
    {
        "choice" => new ChoiceAnswer(
            name,
            a.Choice ?? throw Unreadable($"'{name}' has no choice"),
            Required(name, a.Probabilities).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble())),
        "noul" => new YesNoAnswer(name, a.Probability ?? throw Unreadable($"'{name}' has no probability")),
        "score" => new ScoreAnswer(
            name,
            a.Level ?? throw Unreadable($"'{name}' has no level"),
            [.. Required(name, a.Probabilities).EnumerateArray().Select(p => p.GetDouble())]),
        "box" => new BoxAnswer(name, [.. (a.Boxes ?? throw Unreadable($"'{name}' has no boxes")).Select(b => ToBox(name, b))]),
        _ => throw Unreadable($"'{name}' has unknown type '{a.Type}'"),
    };

    private static Box ToBox(string name, JevBox? b) =>
        b is { Image: { } image, X: { } x, Y: { } y, W: { } w, H: { } h, Confidence: { } confidence }
            ? new Box(image, x, y, w, h, confidence)
            : throw Unreadable($"'{name}' has a box missing image, x, y, w, h or confidence");

    private static JsonElement Required(string name, JsonElement? probabilities) =>
        probabilities ?? throw Unreadable($"'{name}' has no probabilities");

    private static SystemOneException Unreadable(string detail) => new($"Jev returned an unreadable body: {detail}");
}
