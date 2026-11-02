using static Fulcrum.Tests.Triage;
using static Fulcrum.Tests.IdCheck;

namespace Fulcrum.Tests.Rules;

public class ConditionTests
{
    public static TheoryData<Condition, string> Leaves => new()
    {
        { TeamQ.Is(Team.Billing), "team=Billing" },
        { TeamQ.IsNot(Team.Billing), "team!=Billing" },
        { TeamQ.ConfidenceAbove(0.8), "team.confidence>0.8" },
        { SafeQ.Above(0.85), "safe_to_automate>0.85" },
        { SafeQ.Below(0.85), "safe_to_automate<0.85" },
        { UrgencyQ.Is(Urgency.Today), "urgency=Today" },
        { UrgencyQ.IsNot(Urgency.Today), "urgency!=Today" },
        { UrgencyQ.AtLeast(Urgency.ThisWeek), "urgency>=ThisWeek" },
        { UrgencyQ.AtMost(Urgency.ThisWeek), "urgency<=ThisWeek" },
        { UrgencyQ.ConfidenceAbove(0.5), "urgency.confidence>0.5" },
        { FaceQ.Found(), "face.found" },
        { FaceQ.None(), "face.none" },
        { FaceQ.FoundAbove(0.8), "face.found>0.8" },
        { FaceQ.Count().Is(1), "face.count=1" },
        { FaceQ.Count().AtLeast(1), "face.count>=1" },
        { FaceQ.Count().Above(1), "face.count>1" },
        { FaceQ.Count().Below(1), "face.count<1" },
    };

    [Theory]
    [MemberData(nameof(Leaves))]
    public void Renders_leaf(Condition condition, string expected)
    {
        Assert.Equal(expected, condition.ToString());
    }

    [Fact]
    public void And_of_or_parenthesises_the_or()
    {
        var c = (FaceQ.None() || FaceQ.Found()) && SafeQ.Above(0.5);

        Assert.Equal("(face.none || face.found) && safe_to_automate>0.5", c.ToString());
    }

    [Fact]
    public void Or_of_and_parenthesises_the_and()
    {
        var c = FaceQ.None() || (FaceQ.Found() && SafeQ.Above(0.5));

        Assert.Equal("face.none || (face.found && safe_to_automate>0.5)", c.ToString());
    }

    [Fact]
    public void Same_operator_nesting_is_not_parenthesised()
    {
        var c = TeamQ.Is(Team.Billing) && SafeQ.Above(0.5) && FaceQ.Found();

        Assert.Equal("team=Billing && safe_to_automate>0.5 && face.found", c.ToString());
    }

    [Fact]
    public void Not_of_compound_parenthesises()
    {
        Assert.Equal("!(face.none || face.found)", (!(FaceQ.None() || FaceQ.Found())).ToString());
        Assert.Equal("!face.none", (!FaceQ.None()).ToString());
    }

    [Fact]
    public void Double_ampersand_builds_And_not_bool()
    {
        var c = TeamQ.Is(Team.Billing) && SafeQ.Above(0.85);

        Assert.IsType<AndCondition>(c);
    }

    [Fact]
    public void Double_pipe_builds_Or()
    {
        var c = TeamQ.Is(Team.Billing) || SafeQ.Above(0.85);

        Assert.IsType<OrCondition>(c);
    }

    [Fact]
    public void Score_condition_on_undeclared_level_throws_FulcrumException()
    {
        var partial = Question.Score<Urgency>("u", "?", (Urgency.CanWait, "c"), (Urgency.Today, "t"));

        Assert.Throws<FulcrumException>(() => partial.AtLeast(Urgency.ThisWeek));
    }
}
