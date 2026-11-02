using static Fulcrum.Tests.Triage;
using static Fulcrum.Tests.IdCheck;

namespace Fulcrum.Tests.Rules;

public class ConditionEvaluatorTests
{
    private enum Reversed { High = 0, Low = 1 }

    private static readonly Answers Fixture = new(new Dictionary<string, Answer>
    {
        ["team"] = new ChoiceAnswer("team", "Billing", new Dictionary<string, double> { ["Billing"] = 0.9, ["Technical"] = 0.07, ["Sales"] = 0.03 }),
        ["urgency"] = new ScoreAnswer("urgency", 1, [0.3, 0.6, 0.1]),
        ["safe_to_automate"] = new YesNoAnswer("safe_to_automate", 0.95),
        ["face"] = new BoxAnswer("face", [new Box(0, 0.62, 0.18, 0.21, 0.30, 0.97)]),
    });

    private static bool Holds(Condition c, Answers? answers = null) => ConditionEvaluator.Evaluate(c, answers ?? Fixture).Satisfied;

    [Fact]
    public void Choice_is_matches_chosen_member()
    {
        Assert.True(Holds(TeamQ.Is(Team.Billing)));
        Assert.False(Holds(TeamQ.Is(Team.Sales)));
    }

    [Fact]
    public void Choice_is_not()
    {
        Assert.True(Holds(TeamQ.IsNot(Team.Sales)));
        Assert.False(Holds(TeamQ.IsNot(Team.Billing)));
    }

    [Fact]
    public void Confidence_above_is_strict()
    {
        Assert.False(Holds(TeamQ.ConfidenceAbove(0.9)));
        Assert.True(Holds(TeamQ.ConfidenceAbove(0.89)));
        Assert.True(Holds(UrgencyQ.ConfidenceAbove(0.5)));
    }

    [Fact]
    public void YesNo_above_and_below_are_strict()
    {
        Assert.False(Holds(SafeQ.Above(0.95)));
        Assert.True(Holds(SafeQ.Above(0.94)));
        Assert.False(Holds(SafeQ.Below(0.95)));
        Assert.True(Holds(SafeQ.Below(0.96)));
    }

    [Fact]
    public void Score_is_and_bounds_use_the_declared_scale()
    {
        Assert.True(Holds(UrgencyQ.Is(Urgency.ThisWeek)));
        Assert.True(Holds(UrgencyQ.IsNot(Urgency.Today)));
        Assert.True(Holds(UrgencyQ.AtLeast(Urgency.ThisWeek)));
        Assert.False(Holds(UrgencyQ.AtLeast(Urgency.Today)));
        Assert.True(Holds(UrgencyQ.AtMost(Urgency.ThisWeek)));
        Assert.False(Holds(UrgencyQ.AtMost(Urgency.CanWait)));
    }

    [Fact]
    public void Score_at_least_compares_declared_position_not_enum_value()
    {
        var q = Question.Score<Reversed>("r", "?", (Reversed.Low, "low"), (Reversed.High, "high"));
        Answers At(int level) => new(new Dictionary<string, Answer> { ["r"] = new ScoreAnswer("r", level, [0.5, 0.5]) });

        Assert.True(Holds(q.AtLeast(Reversed.High), At(1)));
        Assert.False(Holds(q.AtLeast(Reversed.High), At(0)));
    }

    [Fact]
    public void Box_found_none_count()
    {
        Assert.True(Holds(FaceQ.Found()));
        Assert.False(Holds(FaceQ.None()));
        Assert.True(Holds(FaceQ.Count().Is(1)));
        Assert.True(Holds(FaceQ.Count().AtLeast(1)));
        Assert.False(Holds(FaceQ.Count().Above(1)));
        Assert.True(Holds(FaceQ.Count().Below(2)));

        var none = new Answers(new Dictionary<string, Answer> { ["face"] = new BoxAnswer("face", []) });
        Assert.True(Holds(FaceQ.None(), none));
        Assert.False(Holds(FaceQ.Found(), none));
    }

    [Fact]
    public void Box_found_above_uses_best_box()
    {
        var two = new Answers(new Dictionary<string, Answer>
        {
            ["face"] = new BoxAnswer("face", [new Box(0, 0, 0, 0.1, 0.1, 0.4), new Box(0, 0.5, 0.5, 0.1, 0.1, 0.85)]),
        });

        Assert.True(Holds(FaceQ.FoundAbove(0.8), two));
        Assert.False(Holds(FaceQ.FoundAbove(0.9), two));
        Assert.Equal("best 0.85", ConditionEvaluator.Evaluate(FaceQ.FoundAbove(0.9), two).Leaves.Single().Observed);
    }

    [Fact]
    public void Not_inverts()
    {
        Assert.False(Holds(!TeamQ.Is(Team.Billing)));
        Assert.True(Holds(!FaceQ.None()));
    }

    [Fact]
    public void Evaluate_returns_one_trace_per_leaf_with_observed_values()
    {
        var (satisfied, leaves) = ConditionEvaluator.Evaluate(TeamQ.Is(Team.Sales) && SafeQ.Above(0.85) || FaceQ.Found(), Fixture);

        Assert.True(satisfied);
        Assert.Equal(3, leaves.Count);
        Assert.Equal(["team=Sales", "safe_to_automate>0.85", "face.found"], leaves.Select(l => l.Text));
        Assert.Equal(["Billing", "0.95", "1 box"], leaves.Select(l => l.Observed));
        Assert.Equal([false, true, true], leaves.Select(l => l.Satisfied));
    }

    [Fact]
    public void ReferencedQuestions_is_distinct_in_first_reference_order()
    {
        var c = UrgencyQ.Is(Urgency.Today) || TeamQ.Is(Team.Billing) && SafeQ.Above(0.85) && UrgencyQ.AtLeast(Urgency.CanWait);

        Assert.Equal(["urgency", "team", "safe_to_automate"], ConditionEvaluator.ReferencedQuestions(c).Select(q => q.Name));
    }
}
