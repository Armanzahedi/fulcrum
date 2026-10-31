namespace Fulcrum.Tests;

public class QuestionsTests
{
    [Fact]
    public void Choice_factory_preserves_option_order()
    {
        Assert.Equal([Team.Billing, Team.Technical, Team.Sales], Triage.TeamQ.Options.Select(o => o.Value));
        Assert.Equal("Bugs, errors, integrations, outages", Triage.TeamQ.Options[1].Description);
    }

    [Fact]
    public void Score_factory_preserves_level_order()
    {
        var q = Question.Score<Urgency>("u", "?", (Urgency.Today, "t"), (Urgency.CanWait, "c"), (Urgency.ThisWeek, "w"));

        Assert.Equal([Urgency.Today, Urgency.CanWait, Urgency.ThisWeek], q.Levels.Select(l => l.Value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Factories_reject_empty_name(string name)
    {
        Assert.Throws<ArgumentException>(() => Question.Choice<Team>(name, "?", (Team.Billing, "b"), (Team.Sales, "s")));
        Assert.Throws<ArgumentException>(() => Question.YesNo(name, "?"));
        Assert.Throws<ArgumentException>(() => Question.Score<Urgency>(name, "?", (Urgency.CanWait, "c"), (Urgency.Today, "t")));
        Assert.Throws<ArgumentException>(() => Question.Box(name, "?"));
    }
}
