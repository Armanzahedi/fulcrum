namespace Fulcrum.Testing;

public sealed record FakeRequest(Content Content, IReadOnlyList<Question> Questions);

internal sealed class UnscriptedQuestionException(string message) : InvalidOperationException(message);

public sealed class FakeSystemOne : ISystemOne
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Answer> _answers = [];
    private readonly List<FakeRequest> _requests = [];
    private Exception? _failure;

    public IReadOnlyList<FakeRequest> Requests
    {
        get
        {
            lock (_lock)
                return [.. _requests];
        }
    }

    public FakeSystemOne Answer<TEnum>(ChoiceQuestion<TEnum> q, TEnum value, double confidence) where TEnum : struct, Enum
    {
        var names = q.Choices.Select(c => c.Name).ToList();
        var chosen = NameOf(value);
        return Set(new ChoiceAnswer(q.Name, chosen, names.ToDictionary(n => n, n => Share(n == chosen, confidence, names.Count))));
    }

    public FakeSystemOne Answer<TEnum>(ChoiceQuestion<TEnum> q, IReadOnlyDictionary<TEnum, double> probabilities) where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(probabilities);
        return Set(new ChoiceAnswer(q.Name, NameOf(probabilities.MaxBy(p => p.Value).Key), probabilities.ToDictionary(p => NameOf(p.Key), p => p.Value)));
    }

    public FakeSystemOne Answer(YesNoQuestion q, double probability) => Set(new YesNoAnswer(q.Name, probability));

    public FakeSystemOne Answer(BoxQuestion q, params ReadOnlySpan<Box> boxes) => Set(new BoxAnswer(q.Name, boxes.ToArray()));

    public FakeSystemOne Answer<TEnum>(ScoreQuestion<TEnum> q, TEnum level, double confidence) where TEnum : struct, Enum
    {
        var position = PositionOf(q, level);
        return Set(new ScoreAnswer(q.Name, position, [.. Enumerable.Range(0, q.Levels.Count).Select(i => Share(i == position, confidence, q.Levels.Count))]));
    }

    public FakeSystemOne Answer<TEnum>(ScoreQuestion<TEnum> q, IReadOnlyDictionary<TEnum, double> probabilities) where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(probabilities);
        var distribution = q.Levels.Select(l => probabilities.GetValueOrDefault(l.Value)).ToArray();
        return Set(new ScoreAnswer(q.Name, PositionOf(q, probabilities.MaxBy(p => p.Value).Key), distribution));
    }

    public FakeSystemOne Fail(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (_lock)
            _failure = exception;
        return this;
    }

    public Task<Answers> AskAsync(Content state, IReadOnlyList<Question> questions, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<Answers>(cancellationToken);

        lock (_lock)
        {
            _requests.Add(new FakeRequest(state, [.. questions]));
            if (_failure is { } failure)
            {
                _failure = null;
                return Task.FromException<Answers>(failure);
            }

            var raw = new Dictionary<string, Answer>();
            foreach (var q in questions)
            {
                if (!_answers.TryGetValue(q.Name, out var answer))
                    return Task.FromException<Answers>(new UnscriptedQuestionException($"FakeSystemOne has no answer scripted for '{q.Name}'."));
                raw[q.Name] = answer;
            }
            return Task.FromResult(new Answers(raw));
        }
    }

    private FakeSystemOne Set(Answer answer)
    {
        lock (_lock)
            _answers[answer.Question] = answer;
        return this;
    }

    private static double Share(bool chosen, double confidence, int count) => chosen ? confidence : (1 - confidence) / (count - 1);

    private static string NameOf<TEnum>(TEnum value) where TEnum : struct, Enum => Enum.GetName(value) ?? value.ToString();

    private static int PositionOf<TEnum>(ScoreQuestion<TEnum> q, TEnum level) where TEnum : struct, Enum
    {
        for (var i = 0; i < q.Levels.Count; i++)
        {
            if (EqualityComparer<TEnum>.Default.Equals(q.Levels[i].Value, level))
                return i;
        }
        throw new ArgumentException($"{level} is not a declared level of '{q.Name}'.", nameof(level));
    }
}
