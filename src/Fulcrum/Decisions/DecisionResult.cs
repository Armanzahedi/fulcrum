using System.Text;

namespace Fulcrum;

public sealed class DecisionResult<TOutcome> where TOutcome : struct, Enum
{
    internal DecisionResult(
        string decision,
        TOutcome outcome,
        string? matchedRule,
        double? confidence,
        Answers answers,
        Exception? error,
        IReadOnlyList<RuleTrace> traces,
        IReadOnlyList<Question> asked,
        string? defaultOutcomeName)
    {
        Decision = decision;
        Outcome = outcome;
        MatchedRule = matchedRule;
        Confidence = confidence;
        Answers = answers;
        Error = error;
        Traces = traces;
        Asked = asked;
        DefaultOutcomeName = defaultOutcomeName;
    }

    public string Decision { get; }

    public TOutcome Outcome { get; }

    public string? MatchedRule { get; }

    public double? Confidence { get; }

    public Answers Answers { get; }

    public bool IsFallback => Error is not null;

    public Exception? Error { get; }

    internal IReadOnlyList<RuleTrace> Traces { get; }

    internal IReadOnlyList<Question> Asked { get; }

    internal string? DefaultOutcomeName { get; }

    public string Explain()
    {
        var sb = new StringBuilder();
        var confidence = Confidence is { } c ? ConditionEvaluator.Format(c) : "n/a";
        Line(sb, $"{Decision}  outcome {Name(Outcome)}  rule {MatchedRule ?? "-"}  confidence {confidence}");
        Line(sb, "");
        if (Error is not null)
        {
            Line(sb, $"error  {Error.GetType().Name}: {Error.Message}");
            return sb.ToString();
        }
        RenderAnswers(sb, Answers, Asked);
        Line(sb, "");
        RenderRules(sb, Traces, MatchedRule == "default", DefaultOutcomeName);
        return sb.ToString();
    }

    private static void RenderAnswers(StringBuilder sb, Answers answers, IReadOnlyList<Question> asked)
    {
        Line(sb, "answers");
        var width = asked.Max(q => q.Name.Length) + 3;
        var valueWidth = asked.Max(q => answers.Raw[q.Name] switch
        {
            ChoiceAnswer a => a.Choice.Length,
            ScoreAnswer a => q.Choices[a.Level].Name.Length,
            _ => 0,
        });
        foreach (var q in asked)
        {
            var prefix = $"  {q.Name.PadRight(width)}";
            switch (answers.Raw[q.Name])
            {
                case ChoiceAnswer a:
                    Line(sb, $"{prefix}{a.Choice.PadRight(valueWidth)}  {F(a.Confidence)}   {Distribution(q.Choices.Select(o => (o.Name, a.Probabilities[o.Name])))}");
                    break;
                case ScoreAnswer a:
                    Line(sb, $"{prefix}{q.Choices[a.Level].Name.PadRight(valueWidth)}  {F(a.Confidence)}   {Distribution(q.Choices.Select((o, i) => (o.Name, a.Probabilities[i])))}");
                    break;
                case YesNoAnswer a:
                    Line(sb, $"{prefix}p(yes) {F(a.Probability)}");
                    break;
                case BoxAnswer { Boxes.Count: 0 }:
                    Line(sb, $"{prefix}none");
                    break;
                case BoxAnswer a:
                    var count = ConditionEvaluator.BoxCountText(a.Boxes.Count);
                    var indent = new string(' ', prefix.Length + count.Length + 3);
                    for (var i = 0; i < a.Boxes.Count; i++)
                    {
                        var b = a.Boxes[i];
                        Line(sb, $"{(i == 0 ? $"{prefix}{count}   " : indent)}({F(b.X)}, {F(b.Y)}, {F(b.Width)}, {F(b.Height)}) {F(b.Confidence)}");
                    }
                    break;
            }
        }
    }

    private static void RenderRules(StringBuilder sb, IReadOnlyList<RuleTrace> traces, bool defaultMatched, string? defaultOutcome)
    {
        Line(sb, "rules");
        var nameWidth = traces.Select(t => t.Name.Length).Append("default".Length).Max() + 3;
        var conditionWidth = traces.Select(t => t.ConditionText.Length).DefaultIfEmpty(0).Max() + 3;
        var leafIndent = new string(' ', 4 + nameWidth);
        foreach (var t in traces)
        {
            var head = $"  {Marker(t.Status)} {t.Name.PadRight(nameWidth)}";
            var compound = t.Leaves.Count > 1;
            if (t.Status == RuleStatus.NotMatched && !compound && t.Observed is { } observed)
                Line(sb, $"{head}{t.ConditionText.PadRight(conditionWidth)}{(t.Leaves.Count == 1 ? $"observed {observed}" : observed)}");
            else
                Line(sb, $"{head}{t.ConditionText}");

            if (compound && t.Status != RuleStatus.NotEvaluated)
            {
                var textWidth = t.Leaves.Max(l => l.Text.Length) + 3;
                var observedWidth = t.Leaves.Max(l => l.Observed.Length) + 3;
                foreach (var leaf in t.Leaves)
                    Line(sb, $"{leafIndent}{leaf.Text.PadRight(textWidth)}{leaf.Observed.PadRight(observedWidth)}{(leaf.Satisfied ? "✓" : "✗")}");
            }
        }
        Line(sb, $"  {(defaultMatched ? "✓" : "·")} {"default".PadRight(nameWidth)}{defaultOutcome}");
    }

    private static string Distribution(IEnumerable<(string Name, double P)> entries) =>
        $"{{{string.Join(", ", entries.Select(e => $"{e.Name} {F(e.P)}"))}}}";

    private static string Marker(RuleStatus status) => status switch
    {
        RuleStatus.Matched => "✓",
        RuleStatus.NotMatched => "✗",
        _ => "·",
    };

    private static string F(double value) => ConditionEvaluator.Format(value);

    private static string Name(TOutcome outcome) => Enum.GetName(outcome) ?? outcome.ToString();

    private static void Line(StringBuilder sb, string text) => sb.Append(text).Append('\n');
}
