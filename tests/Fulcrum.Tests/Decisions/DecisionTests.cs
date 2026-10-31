using Fulcrum.Testing;
using static Fulcrum.Tests.Triage;
using static Fulcrum.Tests.IdCheck;

namespace Fulcrum.Tests.Decisions;

public class DecisionTests
{
    private static readonly Ticket ticket = new("T-1", "Refund", "Please refund order 123");
    private static readonly IdDocument document = new("A-1", [1, 2, 3], "image/png", "Passport");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Answers Raw(params Answer[] answers) => new(answers.ToDictionary(a => a.Question));

    internal sealed class LambdaSystemOne(Func<Content, IReadOnlyList<Question>, CancellationToken, Task<Answers>> ask) : ISystemOne
    {
        public Task<Answers> AskAsync(Content state, IReadOnlyList<Question> questions, CancellationToken cancellationToken) =>
            ask(state, questions, cancellationToken);
    }

    private sealed class TriageWithoutFallback : Decision<Ticket, TriageOutcome>
    {
        protected override Content Describe(Ticket t) => t.Body;

        protected override void Define(IRules<TriageOutcome> r)
        {
            r.When(TeamQ.Is(Team.Billing) && SafeQ.Above(0.85)).Then(TriageOutcome.AutoReply);
            r.Default(TriageOutcome.HumanReview);
        }
    }

    private sealed class SafeOnly : Decision<Ticket, TriageOutcome>
    {
        protected override Content Describe(Ticket t) => t.Body;

        protected override void Define(IRules<TriageOutcome> r)
        {
            r.When(SafeQ.Above(0.85)).Then(TriageOutcome.AutoReply);
            r.Default(TriageOutcome.HumanReview);
        }
    }

    private sealed class FaceInText : Decision<Ticket, TriageOutcome>
    {
        protected override Content Describe(Ticket t) => t.Body;

        protected override void Define(IRules<TriageOutcome> r)
        {
            r.When(FaceQ.Found()).Then(TriageOutcome.AutoReply);
            r.Default(TriageOutcome.HumanReview);
        }
    }

    private sealed class DescribeThrows : Decision<Ticket, TriageOutcome>
    {
        protected override Content Describe(Ticket t) => throw new InvalidOperationException("describe bug");

        protected override void Define(IRules<TriageOutcome> r)
        {
            r.When(SafeQ.Above(0.85)).Then(TriageOutcome.AutoReply);
            r.Default(TriageOutcome.HumanReview);
            r.OnError(TriageOutcome.HumanReview);
        }
    }

    private sealed class CountingDefines : Decision<Ticket, TriageOutcome>
    {
        private int _defines;

        public int Defines => _defines;

        protected override Content Describe(Ticket t) => t.Body;

        protected override void Define(IRules<TriageOutcome> r)
        {
            Interlocked.Increment(ref _defines);
            Thread.Sleep(50);
            r.When(SafeQ.Above(0.85)).Then(TriageOutcome.AutoReply);
            r.Default(TriageOutcome.HumanReview);
        }
    }

    [Fact]
    public async Task Confident_billing_ticket_is_auto_replied()
    {
        var fake = new FakeSystemOne()
            .Answer(Triage.TeamQ, Team.Billing, confidence: 0.90)
            .Answer(Triage.SafeQ, 0.95)
            .Answer(Triage.UrgencyQ, Urgency.CanWait, confidence: 0.80);

        var result = await new Triage().DecideAsync(ticket, fake, Ct);

        Assert.Equal(TriageOutcome.AutoReply, result.Outcome);
        Assert.Equal("AutoReply", result.MatchedRule);
        Assert.Equal(["urgency", "team", "safe_to_automate"], fake.Requests.Single().Questions.Select(q => q.Name));
    }

    [Fact]
    public async Task Provider_failure_falls_back_to_human_review()
    {
        var fake = new FakeSystemOne().Fail(new SystemOneException("503"));

        var result = await new Triage().DecideAsync(ticket, fake, Ct);

        Assert.Equal(TriageOutcome.HumanReview, result.Outcome);
        Assert.True(result.IsFallback);
        Assert.NotNull(result.Error);
        Assert.Null(result.MatchedRule);
        Assert.Null(result.Confidence);
        Assert.Same(Answers.Empty, result.Answers);
    }

    [Fact]
    public async Task Rules_are_first_match()
    {
        var fake = new FakeSystemOne()
            .Answer(TeamQ, Team.Billing, 0.9)
            .Answer(SafeQ, 0.95)
            .Answer(UrgencyQ, Urgency.Today, 0.8);

        var result = await new Triage().DecideAsync(ticket, fake, Ct);

        Assert.Equal(TriageOutcome.PageOnCall, result.Outcome);
        Assert.Equal("PageOnCall", result.MatchedRule);
    }

    [Fact]
    public async Task Default_fires_when_nothing_matches_with_MatchedRule_default()
    {
        var fake = new FakeSystemOne()
            .Answer(TeamQ, Team.Sales, 0.9)
            .Answer(SafeQ, 0.95)
            .Answer(UrgencyQ, Urgency.CanWait, 0.8);

        var result = await new Triage().DecideAsync(ticket, fake, Ct);

        Assert.Equal(TriageOutcome.HumanReview, result.Outcome);
        Assert.Equal("default", result.MatchedRule);
        Assert.False(result.IsFallback);
    }

    [Fact]
    public async Task Low_confidence_rule_matches_when_any_question_is_uncertain()
    {
        var fake = new FakeSystemOne()
            .Answer(TeamQ, Team.Billing, 0.9)
            .Answer(SafeQ, 0.95)
            .Answer(UrgencyQ, Urgency.CanWait, 0.5);

        var result = await new Triage().DecideAsync(ticket, fake, Ct);

        Assert.Equal("low-confidence", result.MatchedRule);
        Assert.Equal(0.5, result.Confidence);
    }

    [Fact]
    public async Task Confidence_is_min_over_matched_rules_non_box_questions()
    {
        var fake = new FakeSystemOne()
            .Answer(TeamQ, Team.Billing, 0.9)
            .Answer(SafeQ, 0.95)
            .Answer(UrgencyQ, Urgency.CanWait, 0.8);

        var result = await new Triage().DecideAsync(ticket, fake, Ct);

        Assert.Equal(0.9, result.Confidence);
    }

    [Fact]
    public async Task Confidence_for_default_is_min_over_all_asked_non_box()
    {
        var fake = new FakeSystemOne()
            .Answer(TeamQ, Team.Sales, 0.9)
            .Answer(SafeQ, 0.95)
            .Answer(UrgencyQ, Urgency.CanWait, 0.7);

        var result = await new Triage().DecideAsync(ticket, fake, Ct);

        Assert.Equal("default", result.MatchedRule);
        Assert.Equal(0.7, result.Confidence);
    }

    [Fact]
    public async Task Confidence_is_null_for_box_only_rule()
    {
        var fake = new FakeSystemOne()
            .Answer(FaceQ, new Box(0, 0.1, 0.1, 0.2, 0.2, 0.9), new Box(0, 0.5, 0.5, 0.2, 0.2, 0.8))
            .Answer(KindQ, DocumentKind.Passport, 0.9)
            .Answer(TamperedQ, 0.05);

        var result = await new IdCheck().DecideAsync(document, fake, Ct);

        Assert.Equal(IdCheckOutcome.Retake, result.Outcome);
        Assert.Equal("no-single-face", result.MatchedRule);
        Assert.Null(result.Confidence);
        Assert.False(result.IsFallback);
    }

    [Fact]
    public async Task Image_content_reaches_the_model()
    {
        var fake = new FakeSystemOne()
            .Answer(FaceQ, new Box(0, 0.1, 0.1, 0.2, 0.2, 0.9))
            .Answer(KindQ, DocumentKind.Passport, 0.9)
            .Answer(TamperedQ, 0.05);

        var result = await new IdCheck().DecideAsync(document, fake, Ct);

        Assert.Equal(IdCheckOutcome.Accept, result.Outcome);
        var content = fake.Requests.Single().Content;
        Assert.Equal("Applicant declared document type: Passport", content.Text);
        Assert.Equal("image/png", Assert.Single(content.Images).MediaType);
    }

    [Fact]
    public async Task Only_referenced_questions_are_asked()
    {
        var fake = new FakeSystemOne().Answer(TeamQ, Team.Billing, 0.9).Answer(SafeQ, 0.95);

        await new SafeOnly().DecideAsync(ticket, fake, Ct);

        Assert.Equal(["safe_to_automate"], fake.Requests.Single().Questions.Select(q => q.Name));
    }

    [Fact]
    public async Task Box_question_without_image_throws_FulcrumException()
    {
        var fake = new FakeSystemOne().Answer(FaceQ);

        var ex = await Assert.ThrowsAsync<FulcrumException>(() => new FaceInText().DecideAsync(ticket, fake, Ct));

        Assert.Contains("face", ex.Message);
        Assert.Empty(fake.Requests);
    }

    private static readonly ISystemOne LowerCaseKey = new LambdaSystemOne((_, _, _) => Task.FromResult(Raw(
        new ChoiceAnswer("team", "Billing", new Dictionary<string, double> { ["billing"] = 0.9, ["Technical"] = 0.07, ["Sales"] = 0.03 }),
        new YesNoAnswer("safe_to_automate", 0.95),
        new ScoreAnswer("urgency", 0, [0.8, 0.15, 0.05]))));

    [Fact]
    public async Task Invalid_answer_from_provider_falls_back_when_OnError_declared()
    {
        var result = await new Triage().DecideAsync(ticket, LowerCaseKey, Ct);

        Assert.True(result.IsFallback);
        Assert.Equal(TriageOutcome.HumanReview, result.Outcome);
        Assert.Contains("team", Assert.IsType<SystemOneException>(result.Error).Message);
    }

    [Fact]
    public async Task Invalid_answer_propagates_as_SystemOneException_without_OnError()
    {
        var ex = await Assert.ThrowsAsync<SystemOneException>(() => new TriageWithoutFallback().DecideAsync(ticket, LowerCaseKey, Ct));

        Assert.NotEmpty(ex.Violations);
    }

    [Fact]
    public async Task Provider_exception_propagates_without_OnError()
    {
        var failure = new SystemOneException("503");

        Assert.Same(failure, await Assert.ThrowsAsync<SystemOneException>(() => new TriageWithoutFallback().DecideAsync(ticket, new FakeSystemOne().Fail(failure), Ct)));
    }

    [Fact]
    public async Task Unbound_DecideAsync_throws_FulcrumException_naming_test_overload()
    {
        var ex = await Assert.ThrowsAsync<FulcrumException>(() => new Triage().DecideAsync(ticket, Ct));

        Assert.Contains("DecideAsync(state, model)", ex.Message);
    }

    [Fact]
    public async Task Cancelled_token_propagates_OperationCanceledException_not_fallback()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var model = new LambdaSystemOne((_, _, ct) => throw new OperationCanceledException(ct));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new Triage().DecideAsync(ticket, model, cts.Token));
    }

    [Fact]
    public async Task Exception_in_Describe_propagates_even_with_OnError()
    {
        var fake = new FakeSystemOne().Answer(SafeQ, 0.9);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new DescribeThrows().DecideAsync(ticket, fake, Ct));

        Assert.Equal("describe bug", ex.Message);
    }

    [Fact]
    public async Task Concurrent_first_use_builds_shape_once()
    {
        var decision = new CountingDefines();
        var fake = new FakeSystemOne().Answer(SafeQ, 0.9);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => decision.DecideAsync(ticket, fake, Ct), Ct)));

        Assert.Equal(1, decision.Defines);
        Assert.All(results, r => Assert.Equal(TriageOutcome.AutoReply, r.Outcome));
    }

    [Fact]
    public async Task Unscripted_fake_question_fails_loudly_even_with_OnError()
    {
        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => new Triage().DecideAsync(ticket, new FakeSystemOne(), Ct));

        Assert.Contains("urgency", ex.Message);
    }

    private sealed class EmptyDescribe : Decision<Ticket, TriageOutcome>
    {
        protected override Content Describe(Ticket t) => "";

        protected override void Define(IRules<TriageOutcome> r)
        {
            r.When(SafeQ.Above(0.85)).Then(TriageOutcome.AutoReply);
            r.Default(TriageOutcome.HumanReview);
        }
    }

    [Fact]
    public async Task Non_SystemOne_exception_from_provider_propagates_even_with_OnError()
    {
        var broken = new LambdaSystemOne((_, _, _) => throw new InvalidOperationException("provider bug"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => new Triage().DecideAsync(ticket, broken, Ct));
    }

    [Fact]
    public async Task Empty_content_throws_FulcrumException()
    {
        var ex = await Assert.ThrowsAsync<FulcrumException>(() => new EmptyDescribe().DecideAsync(ticket, new FakeSystemOne(), Ct));

        Assert.Contains("empty content", ex.Message);
    }
}
