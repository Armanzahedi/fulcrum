namespace Fulcrum;

public abstract record Answer(string Question);

public sealed record ChoiceAnswer(string Question, string Choice, IReadOnlyDictionary<string, double> Probabilities) : Answer(Question)
{
    public double Confidence => Probabilities[Choice];
}

public sealed record YesNoAnswer(string Question, double Probability) : Answer(Question)
{
    public double Confidence => Math.Max(Probability, 1 - Probability);
}

public sealed record ScoreAnswer(string Question, int Level, IReadOnlyList<double> Probabilities) : Answer(Question)
{
    public double Confidence => Probabilities[Level];
}

public sealed record BoxAnswer(string Question, IReadOnlyList<Box> Boxes) : Answer(Question);

public readonly record struct Box(int Image, double X, double Y, double Width, double Height, double Confidence);

public sealed record ChoiceAnswer<TEnum>(TEnum Value, double Confidence, IReadOnlyDictionary<TEnum, double> Probabilities)
    where TEnum : struct, Enum;

public sealed record ScoreAnswer<TEnum>(TEnum Value, double Confidence, IReadOnlyDictionary<TEnum, double> Probabilities)
    where TEnum : struct, Enum;

public sealed class Answers
{
    private const double EdgeTolerance = 1e-9;

    public static readonly Answers Empty = new(new Dictionary<string, Answer>());

    public Answers(IReadOnlyDictionary<string, Answer> raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        Raw = raw;
    }

    public IReadOnlyDictionary<string, Answer> Raw { get; }

    public ChoiceAnswer<TEnum> Get<TEnum>(ChoiceQuestion<TEnum> q) where TEnum : struct, Enum
    {
        var a = Find<ChoiceAnswer>(q);
        return new(Enum.Parse<TEnum>(a.Choice), a.Confidence, a.Probabilities.ToDictionary(p => Enum.Parse<TEnum>(p.Key), p => p.Value));
    }

    public YesNoAnswer Get(YesNoQuestion q) => Find<YesNoAnswer>(q);

    public ScoreAnswer<TEnum> Get<TEnum>(ScoreQuestion<TEnum> q) where TEnum : struct, Enum
    {
        var a = Find<ScoreAnswer>(q);
        return new(q.Levels[a.Level].Value, a.Confidence, Enumerable.Range(0, q.Levels.Count).ToDictionary(i => q.Levels[i].Value, i => a.Probabilities[i]));
    }

    public BoxAnswer Get(BoxQuestion q) => Find<BoxAnswer>(q);

    public IReadOnlyList<string> Validate(Content state, IReadOnlyList<Question> questions)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(questions);
        var violations = new List<string>();
        foreach (var q in questions)
        {
            void Report(string problem) => violations.Add($"{q.Name}: {problem}");

            if (!Raw.TryGetValue(q.Name, out var answer) || answer is null)
            {
                Report("not answered");
                continue;
            }

            switch (q.Kind, answer)
            {
                case (QuestionKind.Choice, ChoiceAnswer a):
                    ValidateChoice(q, a, Report);
                    break;
                case (QuestionKind.YesNo, YesNoAnswer a):
                    if (!IsProbability(a.Probability))
                        Report($"probability {a.Probability} is outside [0, 1]");
                    break;
                case (QuestionKind.Score, ScoreAnswer a):
                    ValidateScore(q, a, Report);
                    break;
                case (QuestionKind.Box, BoxAnswer a):
                    ValidateBoxes(state, a, Report);
                    break;
                default:
                    Report($"expected a {q.Kind} answer, got {answer.GetType().Name}");
                    break;
            }
        }
        return violations;
    }

    private static void ValidateChoice(Question q, ChoiceAnswer a, Action<string> report)
    {
        var declared = q.Choices.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        if (!declared.Contains(a.Choice))
            report($"'{a.Choice}' is not a declared option");
        if (a.Probabilities is null)
        {
            report("distribution is missing");
            return;
        }
        foreach (var option in q.Choices.Where(c => !a.Probabilities.ContainsKey(c.Name)))
            report($"option '{option.Name}' is missing from the distribution");
        foreach (var (key, p) in a.Probabilities)
        {
            if (!declared.Contains(key))
                report($"'{key}' in the distribution is not a declared option");
            else if (!IsProbability(p))
                report($"probability of '{key}' is {p}, outside [0, 1]");
        }
    }

    private static void ValidateScore(Question q, ScoreAnswer a, Action<string> report)
    {
        var levels = q.Choices.Count;
        if (a.Level < 0 || a.Level >= levels)
            report($"level {a.Level} is outside [0, {levels - 1}]");
        if (a.Probabilities is null)
        {
            report("distribution is missing");
            return;
        }
        if (a.Probabilities.Count != levels)
            report($"distribution has {a.Probabilities.Count} entries, expected {levels}");
        for (var i = 0; i < a.Probabilities.Count; i++)
        {
            if (!IsProbability(a.Probabilities[i]))
                report($"probability of level {i} is {a.Probabilities[i]}, outside [0, 1]");
        }
    }

    private static void ValidateBoxes(Content state, BoxAnswer a, Action<string> report)
    {
        if (a.Boxes is null)
        {
            report("boxes are missing");
            return;
        }
        for (var i = 0; i < a.Boxes.Count; i++)
        {
            var b = a.Boxes[i];
            if (b.Image < 0 || b.Image >= state.Images.Count)
                report($"box {i} refers to image {b.Image}, but the content has {state.Images.Count}");
            if (!IsProbability(b.X) || !IsProbability(b.Y) || !IsProbability(b.Width) || !IsProbability(b.Height))
                report($"box {i} has a coordinate outside [0, 1]");
            else if (b.X + b.Width > 1 + EdgeTolerance || b.Y + b.Height > 1 + EdgeTolerance)
                report($"box {i} extends past the image edge");
            if (!IsProbability(b.Confidence))
                report($"box {i} confidence {b.Confidence} is outside [0, 1]");
        }
    }

    private static bool IsProbability(double p) => double.IsFinite(p) && p >= 0 && p <= 1;

    private T Find<T>(Question q) where T : Answer
    {
        if (!Raw.TryGetValue(q.Name, out var answer))
            throw new KeyNotFoundException($"No answer for question '{q.Name}'.");
        return answer as T ?? throw new InvalidOperationException($"The answer for '{q.Name}' is a {answer.GetType().Name}, not a {typeof(T).Name}.");
    }
}
