using Fulcrum.Testing;
using static Fulcrum.Tests.Triage;
using static Fulcrum.Tests.IdCheck;

namespace Fulcrum.Tests.Testing;

public class FakeSystemOneTests
{
    private static Task<Answers> Ask(FakeSystemOne fake, Content content, params Question[] questions) =>
        fake.AskAsync(content, questions, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Returns_scripted_answers_for_asked_questions_only()
    {
        var fake = new FakeSystemOne().Answer(TeamQ, Team.Billing, confidence: 0.9).Answer(SafeQ, 0.95);

        var answers = await Ask(fake, "t", SafeQ);

        Assert.Equal(["safe_to_automate"], answers.Raw.Keys);
        Assert.Equal(0.95, answers.Get(SafeQ).Probability);
    }

    [Fact]
    public async Task Choice_shorthand_spreads_remaining_mass_evenly()
    {
        var answers = await Ask(new FakeSystemOne().Answer(TeamQ, Team.Billing, confidence: 0.9), "t", TeamQ);

        var team = answers.Get(TeamQ);
        Assert.Equal(Team.Billing, team.Value);
        Assert.Equal(0.9, team.Confidence);
        Assert.Equal(0.05, team.Probabilities[Team.Technical], 1e-12);
        Assert.Equal(0.05, team.Probabilities[Team.Sales], 1e-12);
        Assert.Empty(answers.Validate("t", [TeamQ]));
    }

    [Fact]
    public async Task Choice_distribution_commits_to_the_most_likely_option()
    {
        var fake = new FakeSystemOne().Answer(TeamQ, new Dictionary<Team, double> { [Team.Billing] = 0.2, [Team.Technical] = 0.7, [Team.Sales] = 0.1 });

        var team = (await Ask(fake, "t", TeamQ)).Get(TeamQ);

        Assert.Equal(Team.Technical, team.Value);
        Assert.Equal(0.7, team.Confidence);
    }

    [Fact]
    public async Task Score_shorthand_spreads_remaining_mass_evenly()
    {
        var answers = await Ask(new FakeSystemOne().Answer(UrgencyQ, Urgency.Today, confidence: 0.8), "t", UrgencyQ);

        var raw = Assert.IsType<ScoreAnswer>(answers.Raw["urgency"]);
        Assert.Equal(2, raw.Level);
        Assert.Equal([0.1, 0.1, 0.8], raw.Probabilities.Select(p => Math.Round(p, 12)));
        Assert.Empty(answers.Validate("t", [UrgencyQ]));
    }

    [Fact]
    public async Task Score_distribution_commits_to_the_most_likely_level()
    {
        var fake = new FakeSystemOne().Answer(UrgencyQ, new Dictionary<Urgency, double> { [Urgency.CanWait] = 0.2, [Urgency.ThisWeek] = 0.5, [Urgency.Today] = 0.3 });

        var urgency = (await Ask(fake, "t", UrgencyQ)).Get(UrgencyQ);

        Assert.Equal(Urgency.ThisWeek, urgency.Value);
        Assert.Equal(0.3, urgency.Probabilities[Urgency.Today]);
    }

    [Fact]
    public async Task Box_answer_with_no_boxes_means_none()
    {
        var answers = await Ask(new FakeSystemOne().Answer(FaceQ), Content.Image(new Uri("https://example.com/a.png")), FaceQ);

        Assert.Empty(answers.Get(FaceQ).Boxes);
    }

    [Fact]
    public async Task Fail_throws_once_then_recovers()
    {
        var failure = new SystemOneException("503");
        var fake = new FakeSystemOne().Answer(SafeQ, 0.5).Fail(failure);

        Assert.Same(failure, await Assert.ThrowsAsync<SystemOneException>(() => Ask(fake, "t", SafeQ)));
        Assert.Equal(0.5, (await Ask(fake, "t", SafeQ)).Get(SafeQ).Probability);
    }

    [Fact]
    public async Task Records_every_request_with_content_and_questions()
    {
        var fake = new FakeSystemOne().Answer(SafeQ, 0.5).Answer(TeamQ, Team.Sales, 0.6);
        Content first = "first";

        await Ask(fake, first, SafeQ);
        await Ask(fake, "second", TeamQ, SafeQ);

        Assert.Equal(2, fake.Requests.Count);
        Assert.Same(first, fake.Requests[0].Content);
        Assert.Equal(["team", "safe_to_automate"], fake.Requests[1].Questions.Select(q => q.Name));
    }

    [Fact]
    public async Task Unscripted_question_throws_naming_it()
    {
        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => Ask(new FakeSystemOne(), "t", TamperedQ));

        Assert.Contains("tampered", ex.Message);
    }
}
