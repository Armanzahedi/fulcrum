using System.Globalization;

namespace Fulcrum;

internal static class ConditionEvaluator
{
    public static (bool Satisfied, IReadOnlyList<LeafTrace> Leaves) Evaluate(Condition c, Answers answers)
    {
        var leaves = new List<LeafTrace>();
        var satisfied = Walk(c, answers, leaves);
        return (satisfied, leaves);
    }

    public static IEnumerable<Question> ReferencedQuestions(Condition c)
    {
        var seen = new HashSet<Question>(ReferenceEqualityComparer.Instance);
        return Leaves(c).Select(l => l.Question).Where(seen.Add).ToList();
    }

    public static IEnumerable<Leaf> Leaves(Condition c) => c switch
    {
        AndCondition a => Leaves(a.Left).Concat(Leaves(a.Right)),
        OrCondition o => Leaves(o.Left).Concat(Leaves(o.Right)),
        NotCondition n => Leaves(n.Inner),
        Leaf l => [l],
        _ => throw new InvalidOperationException($"Unknown condition {c.GetType().Name}"),
    };

    public static double? ConfidenceOf(Answer answer) => answer switch
    {
        ChoiceAnswer a => a.Confidence,
        ScoreAnswer a => a.Confidence,
        YesNoAnswer a => a.Confidence,
        _ => null,
    };

    public static string Format(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    public static string BoxCountText(int count) => count switch
    {
        0 => "none",
        1 => "1 box",
        _ => $"{count} boxes",
    };

    private static bool Walk(Condition c, Answers answers, List<LeafTrace> leaves)
    {
        switch (c)
        {
            case AndCondition a:
                var andLeft = Walk(a.Left, answers, leaves);
                return Walk(a.Right, answers, leaves) && andLeft;
            case OrCondition o:
                var orLeft = Walk(o.Left, answers, leaves);
                return Walk(o.Right, answers, leaves) || orLeft;
            case NotCondition n:
                return !Walk(n.Inner, answers, leaves);
            case Leaf l:
                var (satisfied, observed) = EvaluateLeaf(l, answers.Raw[l.Question.Name]);
                leaves.Add(new LeafTrace(l.ToString(), observed, satisfied));
                return satisfied;
            default:
                throw new InvalidOperationException($"Unknown condition {c.GetType().Name}");
        }
    }

    private static (bool, string) EvaluateLeaf(Leaf leaf, Answer answer) => (leaf, answer) switch
    {
        (ChoiceIs l, ChoiceAnswer a) => ((a.Choice == l.Option) != l.Negated, a.Choice),
        (ConfidenceAbove l, _) => (ConfidenceOf(answer) > l.T, Format(ConfidenceOf(answer)!.Value)),
        (YesNoAbove l, YesNoAnswer a) => (a.Probability > l.T, Format(a.Probability)),
        (YesNoBelow l, YesNoAnswer a) => (a.Probability < l.T, Format(a.Probability)),
        (ScoreIs l, ScoreAnswer a) => ((a.Level == l.Position) != l.Negated, LevelName(l, a)),
        (ScoreAtLeast l, ScoreAnswer a) => (a.Level >= l.Position, LevelName(l, a)),
        (ScoreAtMost l, ScoreAnswer a) => (a.Level <= l.Position, LevelName(l, a)),
        (BoxFound, BoxAnswer a) => (a.Boxes.Count > 0, BoxCountText(a.Boxes.Count)),
        (BoxNone, BoxAnswer a) => (a.Boxes.Count == 0, BoxCountText(a.Boxes.Count)),
        (BoxFoundAbove l, BoxAnswer a) => (a.Boxes.Any(b => b.Confidence > l.T),
            a.Boxes.Count == 0 ? "none" : $"best {Format(a.Boxes.Max(b => b.Confidence))}"),
        (BoxCount l, BoxAnswer a) => (l.Op switch
        {
            CountOp.Is => a.Boxes.Count == l.N,
            CountOp.AtLeast => a.Boxes.Count >= l.N,
            CountOp.Above => a.Boxes.Count > l.N,
            _ => a.Boxes.Count < l.N,
        }, BoxCountText(a.Boxes.Count)),
        _ => throw new InvalidOperationException($"{leaf.Question.Name}: a {answer.GetType().Name} cannot satisfy {leaf}"),
    };

    private static string LevelName(Leaf leaf, ScoreAnswer a) => leaf.Question.Choices[a.Level].Name;
}
