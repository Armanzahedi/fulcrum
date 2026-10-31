using static Fulcrum.Tests.Triage;
using static Fulcrum.Tests.IdCheck;

namespace Fulcrum.Tests.Decisions;

public class DecisionShapeTests
{
    private static DecisionShape<Ticket, TriageOutcome> Build(Action<IRules<TriageOutcome>> define, Action<IReactions<Ticket, TriageOutcome>>? react = null) =>
        DecisionShape<Ticket, TriageOutcome>.Build("Triage", define, react ?? (_ => { }));

    private static void Valid(IRules<TriageOutcome> r)
    {
        r.When(UrgencyQ.Is(Urgency.Today)).Then(TriageOutcome.PageOnCall);
        r.Default(TriageOutcome.HumanReview);
    }

    private static void AssertRejected(string phrase, Action<IRules<TriageOutcome>> define, Action<IReactions<Ticket, TriageOutcome>>? react = null)
    {
        var ex = Assert.Throws<FulcrumException>(() => Build(define, react));
        Assert.StartsWith("Triage: ", ex.Message);
        Assert.Contains(phrase, ex.Message);
    }

    private sealed class Handler : IOutcomeHandler<Ticket, TriageOutcome>
    {
        public Task HandleAsync(Ticket state, DecisionResult<TriageOutcome> result, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public void Builds_rules_in_declaration_order_with_default_names()
    {
        var shape = Build(r =>
        {
            r.WhenAnyConfidenceBelow(0.6).Then(TriageOutcome.HumanReview);
            r.When(UrgencyQ.Is(Urgency.Today)).Then(TriageOutcome.PageOnCall);
            r.Default(TriageOutcome.AutoReply);
            r.OnError(TriageOutcome.HumanReview);
        });

        Assert.Equal(["HumanReview", "PageOnCall"], shape.Rules.Select(r => r.Name));
        Assert.Equal(0.6, shape.Rules[0].AnyConfidenceBelow);
        Assert.Null(shape.Rules[0].Condition);
        Assert.Equal(TriageOutcome.AutoReply, shape.Default);
        Assert.Equal(TriageOutcome.HumanReview, shape.OnError);
    }

    [Fact]
    public void Explicit_name_overrides_default()
    {
        var shape = Build(r =>
        {
            r.When(UrgencyQ.Is(Urgency.Today)).Then(TriageOutcome.PageOnCall, name: "urgent");
            r.Default(TriageOutcome.HumanReview);
        });

        Assert.Equal("urgent", shape.Rules.Single().Name);
        Assert.Null(shape.OnError);
    }

    [Fact]
    public void Questions_are_collected_in_first_reference_order()
    {
        var shape = Build(r =>
        {
            r.When(UrgencyQ.Is(Urgency.Today)).Then(TriageOutcome.PageOnCall);
            r.When(TeamQ.Is(Team.Billing) && SafeQ.Above(0.85) && UrgencyQ.AtMost(Urgency.ThisWeek)).Then(TriageOutcome.AutoReply);
            r.Default(TriageOutcome.HumanReview);
        });

        Assert.Equal(["urgency", "team", "safe_to_automate"], shape.Questions.Select(q => q.Name));
    }

    [Fact]
    public void WhenAnyConfidenceBelow_adds_no_questions()
    {
        var shape = Build(r =>
        {
            r.WhenAnyConfidenceBelow(0.6).Then(TriageOutcome.HumanReview);
            r.When(SafeQ.Above(0.5)).Then(TriageOutcome.AutoReply);
            r.Default(TriageOutcome.HumanReview);
        });

        Assert.Equal(["safe_to_automate"], shape.Questions.Select(q => q.Name));
    }

    [Fact]
    public void Reactions_recorded_per_outcome()
    {
        Func<Ticket, DecisionResult<TriageOutcome>, CancellationToken, Task> page = (_, _, _) => Task.CompletedTask;

        var shape = Build(Valid, r =>
        {
            r.On(TriageOutcome.AutoReply).With<Handler>();
            r.On(TriageOutcome.PageOnCall).With(page);
            r.On(TriageOutcome.HumanReview).Nothing();
        });

        Assert.True(shape.HasReactions);
        Assert.Equal(typeof(Handler), Assert.IsType<HandlerBinding<Ticket, TriageOutcome>.TypeBinding>(shape.Reactions[TriageOutcome.AutoReply]).HandlerType);
        Assert.Same(page, Assert.IsType<HandlerBinding<Ticket, TriageOutcome>.DelegateBinding>(shape.Reactions[TriageOutcome.PageOnCall]).Handler);
        Assert.IsType<HandlerBinding<Ticket, TriageOutcome>.NothingBinding>(shape.Reactions[TriageOutcome.HumanReview]);
    }

    [Fact]
    public void HasReactions_false_when_react_is_empty()
    {
        var shape = Build(Valid);

        Assert.False(shape.HasReactions);
        Assert.Empty(shape.Reactions);
    }

    [Fact]
    public void Rejects_no_rules() => AssertRejected("no rules", r => r.Default(TriageOutcome.HumanReview));

    [Fact]
    public void Rejects_missing_default() => AssertRejected("no Default", r => r.When(SafeQ.Above(0.5)).Then(TriageOutcome.AutoReply));

    [Fact]
    public void Rejects_duplicate_rule_names() => AssertRejected("duplicate rule name 'AutoReply'", r =>
    {
        r.When(SafeQ.Above(0.5)).Then(TriageOutcome.AutoReply);
        r.When(TeamQ.Is(Team.Sales)).Then(TriageOutcome.AutoReply);
        r.Default(TriageOutcome.HumanReview);
    });

    [Fact]
    public void Rejects_rule_named_default() => AssertRejected("'default' is reserved", r =>
    {
        r.When(SafeQ.Above(0.5)).Then(TriageOutcome.AutoReply, name: "default");
        r.Default(TriageOutcome.HumanReview);
    });

    [Fact]
    public void Rejects_distinct_questions_sharing_a_name() => AssertRejected("two different questions are named 'safe_to_automate'", r =>
    {
        r.When(SafeQ.Above(0.5)).Then(TriageOutcome.AutoReply);
        r.When(Question.YesNo("safe_to_automate", "other").Above(0.5)).Then(TriageOutcome.PageOnCall);
        r.Default(TriageOutcome.HumanReview);
    });

    [Fact]
    public void Rejects_choice_with_fewer_than_two_options() => AssertRejected("'solo' needs at least two options", r =>
    {
        r.When(Question.Choice<Team>("solo", "?", (Team.Billing, "b")).Is(Team.Billing)).Then(TriageOutcome.AutoReply);
        r.Default(TriageOutcome.HumanReview);
    });

    [Fact]
    public void Rejects_choice_with_repeated_option() => AssertRejected("'twice' repeats option Billing", r =>
    {
        r.When(Question.Choice<Team>("twice", "?", (Team.Billing, "b"), (Team.Billing, "again")).Is(Team.Billing)).Then(TriageOutcome.AutoReply);
        r.Default(TriageOutcome.HumanReview);
    });

    [Fact]
    public void Rejects_score_with_fewer_than_two_levels() => AssertRejected("'solo' needs at least two levels", r =>
    {
        r.When(Question.Score<Urgency>("solo", "?", (Urgency.Today, "t")).Is(Urgency.Today)).Then(TriageOutcome.PageOnCall);
        r.Default(TriageOutcome.HumanReview);
    });

    [Fact]
    public void Rejects_score_with_repeated_level() => AssertRejected("'twice' repeats level Today", r =>
    {
        r.When(Question.Score<Urgency>("twice", "?", (Urgency.CanWait, "c"), (Urgency.ThisWeek, "w"), (Urgency.Today, "t"), (Urgency.Today, "again")).Is(Urgency.Today)).Then(TriageOutcome.PageOnCall);
        r.Default(TriageOutcome.HumanReview);
    });

    [Fact]
    public void Rejects_score_that_omits_an_enum_member() => AssertRejected("'partial' does not list level ThisWeek", r =>
    {
        r.When(Question.Score<Urgency>("partial", "?", (Urgency.CanWait, "c"), (Urgency.Today, "t")).Is(Urgency.Today)).Then(TriageOutcome.PageOnCall);
        r.Default(TriageOutcome.HumanReview);
    });

    [Fact]
    public void Rejects_choice_condition_on_undeclared_option() => AssertRejected("'team' has no option Sales", r =>
    {
        var q = Question.Choice<Team>("team", "?", (Team.Billing, "b"), (Team.Technical, "t"));
        r.When(q.Is(Team.Sales)).Then(TriageOutcome.AutoReply);
        r.Default(TriageOutcome.HumanReview);
    });

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void Rejects_threshold_outside_unit_interval(double t)
    {
        AssertRejected("outside [0, 1]", r => { r.When(SafeQ.Above(t)).Then(TriageOutcome.AutoReply); r.Default(TriageOutcome.HumanReview); });
        AssertRejected("outside [0, 1]", r => { r.When(SafeQ.Below(t)).Then(TriageOutcome.AutoReply); r.Default(TriageOutcome.HumanReview); });
        AssertRejected("outside [0, 1]", r => { r.When(TeamQ.ConfidenceAbove(t)).Then(TriageOutcome.AutoReply); r.Default(TriageOutcome.HumanReview); });
        AssertRejected("outside [0, 1]", r => { r.When(FaceQ.FoundAbove(t)).Then(TriageOutcome.AutoReply); r.Default(TriageOutcome.HumanReview); });
        AssertRejected("outside [0, 1]", r => { r.WhenAnyConfidenceBelow(t).Then(TriageOutcome.AutoReply); r.When(SafeQ.Above(0.5)).Then(TriageOutcome.PageOnCall); r.Default(TriageOutcome.HumanReview); });
    }

    [Fact]
    public void Rejects_negative_box_count() => AssertRejected("negative count", r =>
    {
        r.When(FaceQ.Count().AtLeast(-1)).Then(TriageOutcome.AutoReply);
        r.Default(TriageOutcome.HumanReview);
    });

    [Fact]
    public void Rejects_partial_react() => AssertRejected("React does not map HumanReview", Valid, r =>
    {
        r.On(TriageOutcome.AutoReply).Nothing();
        r.On(TriageOutcome.PageOnCall).Nothing();
    });

    [Fact]
    public void Rejects_outcome_mapped_twice() => AssertRejected("React maps AutoReply more than once", Valid, r =>
    {
        r.On(TriageOutcome.AutoReply).Nothing();
        r.On(TriageOutcome.AutoReply).With<Handler>();
        r.On(TriageOutcome.PageOnCall).Nothing();
        r.On(TriageOutcome.HumanReview).Nothing();
    });

    [Fact]
    public void Rejects_default_declared_twice() => AssertRejected("Default declared more than once", r =>
    {
        Valid(r);
        r.Default(TriageOutcome.AutoReply);
    });

    [Fact]
    public void Rejects_on_error_declared_twice() => AssertRejected("OnError declared more than once", r =>
    {
        Valid(r);
        r.OnError(TriageOutcome.HumanReview);
        r.OnError(TriageOutcome.HumanReview);
    });

    [Fact]
    public void Rejects_when_without_then() => AssertRejected("When without Then", r =>
    {
        r.When(SafeQ.Above(0.5));
        Valid(r);
    });

    [Fact]
    public void Rejects_decision_that_references_no_questions() => AssertRejected("no rule references a question", r =>
    {
        r.WhenAnyConfidenceBelow(0.6).Then(TriageOutcome.HumanReview);
        r.Default(TriageOutcome.AutoReply);
    });

    [Fact]
    public void Rejects_outcomes_outside_the_enum()
    {
        AssertRejected("42 is not a defined TriageOutcome", r =>
        {
            r.When(SafeQ.Above(0.5)).Then((TriageOutcome)42, name: "odd");
            r.Default(TriageOutcome.HumanReview);
        });
        AssertRejected("43 is not a defined TriageOutcome", r => { Valid(r); r.OnError((TriageOutcome)43); });
        AssertRejected("44 is not a defined TriageOutcome", r =>
        {
            r.When(SafeQ.Above(0.5)).Then(TriageOutcome.AutoReply);
            r.Default((TriageOutcome)44);
        });
        AssertRejected("45 is not a defined TriageOutcome", Valid, r =>
        {
            foreach (var outcome in Enum.GetValues<TriageOutcome>())
                r.On(outcome).Nothing();
            r.On((TriageOutcome)45).Nothing();
        });
    }
}
