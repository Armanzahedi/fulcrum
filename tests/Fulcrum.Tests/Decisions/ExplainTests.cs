using Fulcrum.Testing;
using static Fulcrum.Tests.Triage;
using static Fulcrum.Tests.IdCheck;

namespace Fulcrum.Tests.Decisions;

public class ExplainTests
{
    private static readonly Ticket ticket = new("T-1", "Refund", "Please refund order 123");
    private static readonly IdDocument document = new("A-1", [1, 2, 3], "image/png", "Passport");

    private static FakeSystemOne Confident(Urgency urgency = Urgency.CanWait) => new FakeSystemOne()
        .Answer(TeamQ, new Dictionary<Team, double> { [Team.Billing] = 0.90, [Team.Technical] = 0.07, [Team.Sales] = 0.03 })
        .Answer(SafeQ, 0.95)
        .Answer(UrgencyQ, new Dictionary<Urgency, double>
        {
            [Urgency.CanWait] = urgency == Urgency.CanWait ? 0.80 : 0.15,
            [Urgency.ThisWeek] = 0.05,
            [Urgency.Today] = urgency == Urgency.Today ? 0.80 : 0.15,
        });

    private static async Task<string[]> Explain(FakeSystemOne fake) =>
        (await new Triage().DecideAsync(ticket, fake, TestContext.Current.CancellationToken)).Explain().Split('\n');

    private static async Task<string> ExplainId(FakeSystemOne fake) =>
        (await new IdCheck().DecideAsync(document, fake, TestContext.Current.CancellationToken)).Explain();

    private static FakeSystemOne Document(params Box[] faces) => new FakeSystemOne()
        .Answer(FaceQ, faces)
        .Answer(KindQ, DocumentKind.Passport, 0.9)
        .Answer(TamperedQ, 0.05);

    [Fact]
    public async Task Renders_the_full_explanation()
    {
        var expected = string.Join('\n',
            "Triage  outcome AutoReply  rule AutoReply  confidence 0.90",
            "",
            "answers",
            "  urgency            CanWait  0.80   {CanWait 0.80, ThisWeek 0.05, Today 0.15}",
            "  team               Billing  0.90   {Billing 0.90, Technical 0.07, Sales 0.03}",
            "  safe_to_automate   p(yes) 0.95",
            "",
            "rules",
            "  ✗ low-confidence   any confidence < 0.6                    min 0.80",
            "  ✗ PageOnCall       urgency=Today                           observed CanWait",
            "  ✓ AutoReply        team=Billing && safe_to_automate>0.85",
            "                     team=Billing            Billing   ✓",
            "                     safe_to_automate>0.85   0.95      ✓",
            "  · default          HumanReview",
            "");

        Assert.Equal(expected, string.Join('\n', await Explain(Confident())));
    }

    [Fact]
    public async Task Header_shows_outcome_rule_and_confidence()
    {
        var lines = await Explain(Confident());

        Assert.Equal("Triage  outcome AutoReply  rule AutoReply  confidence 0.90", lines[0]);
    }

    [Fact]
    public async Task Answers_section_renders_each_kind()
    {
        var text = string.Join('\n', await Explain(Confident()));

        Assert.Contains("CanWait  0.80", text);
        Assert.Contains("{Billing 0.90, Technical 0.07, Sales 0.03}", text);
        Assert.Contains("p(yes) 0.95", text);
    }

    [Fact]
    public async Task Box_answers_render_boxes_or_none()
    {
        var one = await ExplainId(Document(new Box(0, 0.62, 0.18, 0.21, 0.30, 0.97)));
        var two = await ExplainId(Document(new Box(0, 0.62, 0.18, 0.21, 0.30, 0.97), new Box(0, 0.1, 0.1, 0.2, 0.2, 0.5)));
        var none = await ExplainId(Document());

        Assert.Contains("  face       1 box   (0.62, 0.18, 0.21, 0.30) 0.97\n", one);
        Assert.Contains("  face       2 boxes   (0.62, 0.18, 0.21, 0.30) 0.97\n                       (0.10, 0.10, 0.20, 0.20) 0.50\n", two);
        Assert.Contains("  face       none\n", none);
    }

    [Fact]
    public async Task Matched_compound_rule_lists_leaves()
    {
        var lines = await Explain(Confident());

        var rule = Array.FindIndex(lines, l => l.StartsWith("  ✓ AutoReply"));
        Assert.Equal(["team=Billing", "Billing", "✓"], lines[rule + 1].Split(' ', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(["safe_to_automate>0.85", "0.95", "✓"], lines[rule + 2].Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task Not_matched_compound_rule_lists_leaves()
    {
        var fake = Confident().Answer(SafeQ, 0.7);

        var lines = await Explain(fake);

        var rule = Array.FindIndex(lines, l => l.StartsWith("  ✗ AutoReply"));
        Assert.Equal(["safe_to_automate>0.85", "0.70", "✗"], lines[rule + 2].Split(' ', StringSplitOptions.RemoveEmptyEntries));
        Assert.StartsWith("  ✓ default", lines[rule + 3]);
    }

    [Fact]
    public async Task Not_matched_single_leaf_rule_shows_observed()
    {
        var text = string.Join('\n', await Explain(Confident()));

        Assert.Contains("observed CanWait", text);
    }

    [Fact]
    public async Task Rules_after_match_are_not_evaluated()
    {
        var lines = await Explain(Confident(Urgency.Today));

        Assert.Contains(lines, l => l.StartsWith("  ✓ PageOnCall"));
        Assert.Contains("  · AutoReply        team=Billing && safe_to_automate>0.85", lines);
        Assert.Contains(lines, l => l.StartsWith("  · default"));
    }

    [Fact]
    public async Task Fallback_renders_error_only()
    {
        var lines = await Explain(new FakeSystemOne().Fail(new SystemOneException("503")));

        Assert.Equal(["Triage  outcome HumanReview  rule -  confidence n/a", "", "error  SystemOneException: 503", ""], lines);
    }

    [Fact]
    public async Task Confidence_na_for_box_only_rule()
    {
        var text = await ExplainId(Document());

        Assert.StartsWith("IdCheck  outcome Retake  rule no-single-face  confidence n/a\n", text);
    }

    [Fact]
    public async Task Answer_values_of_different_lengths_are_aligned()
    {
        var fake = new FakeSystemOne()
            .Answer(TeamQ, Team.Technical, 0.9)
            .Answer(SafeQ, 0.2)
            .Answer(UrgencyQ, Urgency.Today, 0.8);

        var lines = await Explain(fake);

        Assert.Contains("  urgency            Today      0.80   {CanWait 0.10, ThisWeek 0.10, Today 0.80}", lines);
        Assert.Contains("  team               Technical  0.90   {Billing 0.05, Technical 0.90, Sales 0.05}", lines);
        Assert.Contains("  safe_to_automate   p(yes) 0.20", lines);
    }
}
