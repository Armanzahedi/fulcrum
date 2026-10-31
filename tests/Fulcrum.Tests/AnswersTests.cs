using static Fulcrum.Tests.Triage;
using static Fulcrum.Tests.IdCheck;

namespace Fulcrum.Tests;

public class AnswersTests
{
    private static readonly Content Text = "ticket";
    private static readonly Content OneImage = Content.From("doc").WithImage(new byte[] { 1 }, "image/png");

    private static ChoiceAnswer Team(string choice = "Billing", double billing = 0.9, double technical = 0.07, double sales = 0.03) =>
        new("team", choice, new Dictionary<string, double> { ["Billing"] = billing, ["Technical"] = technical, ["Sales"] = sales });

    private static Answers Of(params Answer[] answers) => new(answers.ToDictionary(a => a.Question));

    private static IReadOnlyList<string> AssertViolations(int count, Answers answers, Content content, params Question[] asked)
    {
        var violations = answers.Validate(content, asked);
        Assert.Equal(count, violations.Count);
        return violations;
    }

    [Fact]
    public void Get_choice_parses_enum_and_maps_probabilities()
    {
        var a = Of(Team(sales: 0.03)).Get(TeamQ);

        Assert.Equal(Tests.Team.Billing, a.Value);
        Assert.Equal(0.9, a.Confidence);
        Assert.Equal(0.03, a.Probabilities[Tests.Team.Sales]);
    }

    [Fact]
    public void Get_score_maps_level_index_to_declared_member()
    {
        var a = Of(new ScoreAnswer("urgency", 2, [0.1, 0.2, 0.7])).Get(UrgencyQ);

        Assert.Equal(Urgency.Today, a.Value);
        Assert.Equal(0.7, a.Confidence);
        Assert.Equal(0.2, a.Probabilities[Urgency.ThisWeek]);
    }

    [Fact]
    public void YesNo_confidence_is_max_of_p_and_one_minus_p()
    {
        Assert.Equal(0.7, new YesNoAnswer("safe", 0.3).Confidence);
        Assert.Equal(0.8, new YesNoAnswer("safe", 0.8).Confidence);
    }

    [Fact]
    public void Get_missing_question_throws_KeyNotFound_naming_it()
    {
        var ex = Assert.Throws<KeyNotFoundException>(() => Of(Team()).Get(SafeQ));

        Assert.Contains("safe_to_automate", ex.Message);
    }

    [Fact]
    public void Empty_has_no_answers()
    {
        Assert.Empty(Answers.Empty.Raw);
    }

    [Fact]
    public void Validate_passes_for_well_formed_answers()
    {
        var answers = Of(Team(), new YesNoAnswer("safe_to_automate", 0.95), new ScoreAnswer("urgency", 0, [0.8, 0.15, 0.05]));

        AssertViolations(0, answers, Text, UrgencyQ, TeamQ, SafeQ);
    }

    [Fact]
    public void Validate_reports_missing_question()
    {
        var v = AssertViolations(1, Of(Team()), Text, TeamQ, SafeQ);

        Assert.Contains("safe_to_automate", v[0]);
    }

    [Fact]
    public void Validate_reports_wrong_kind()
    {
        var v = AssertViolations(1, Of(new YesNoAnswer("team", 0.5)), Text, TeamQ);

        Assert.Contains("team", v[0]);
    }

    [Fact]
    public void Validate_reports_undeclared_choice()
    {
        var v = AssertViolations(1, Of(Team(choice: "billing")), Text, TeamQ);

        Assert.Contains("team", v[0]);
        Assert.Contains("billing", v[0]);
    }

    [Fact]
    public void Validate_reports_option_missing_from_distribution()
    {
        var answer = new ChoiceAnswer("team", "Billing", new Dictionary<string, double> { ["Billing"] = 0.9, ["Technical"] = 0.1 });

        var v = AssertViolations(1, Of(answer), Text, TeamQ);

        Assert.Contains("team", v[0]);
        Assert.Contains("Sales", v[0]);
    }

    [Fact]
    public void Validate_reports_undeclared_key_in_distribution()
    {
        var answer = new ChoiceAnswer("team", "Billing", new Dictionary<string, double> { ["Billing"] = 0.9, ["Technical"] = 0.05, ["Sales"] = 0.03, ["Legal"] = 0.02 });

        var v = AssertViolations(1, Of(answer), Text, TeamQ);

        Assert.Contains("Legal", v[0]);
    }

    [Fact]
    public void Validate_reports_probability_out_of_range()
    {
        var v = AssertViolations(1, Of(new YesNoAnswer("safe_to_automate", 1.2)), Text, SafeQ);

        Assert.Contains("safe_to_automate", v[0]);
    }

    [Fact]
    public void Validate_rejects_NaN_and_infinity()
    {
        var answers = Of(new YesNoAnswer("safe_to_automate", double.NaN), Team(technical: double.PositiveInfinity));

        var v = AssertViolations(2, answers, Text, SafeQ, TeamQ);

        Assert.Contains("safe_to_automate", v[0]);
        Assert.Contains("team", v[1]);
    }

    [Fact]
    public void Validate_reports_score_level_out_of_range()
    {
        var v = AssertViolations(1, Of(new ScoreAnswer("urgency", 3, [0.1, 0.2, 0.7])), Text, UrgencyQ);

        Assert.Contains("urgency", v[0]);
    }

    [Fact]
    public void Validate_reports_score_distribution_wrong_length()
    {
        var v = AssertViolations(1, Of(new ScoreAnswer("urgency", 1, [0.3, 0.7])), Text, UrgencyQ);

        Assert.Contains("urgency", v[0]);
    }

    [Fact]
    public void Validate_reports_box_image_index_out_of_range()
    {
        var v = AssertViolations(1, Of(new BoxAnswer("face", [new Box(1, 0.1, 0.1, 0.2, 0.2, 0.9)])), OneImage, FaceQ);

        Assert.Contains("face", v[0]);
    }

    [Fact]
    public void Validate_reports_box_exceeding_right_edge()
    {
        var v = AssertViolations(1, Of(new BoxAnswer("face", [new Box(0, 0.8, 0.1, 0.3, 0.2, 0.9)])), OneImage, FaceQ);

        Assert.Contains("face", v[0]);
    }

    [Fact]
    public void Validate_accepts_box_touching_the_edge_despite_rounding()
    {
        AssertViolations(0, Of(new BoxAnswer("face", [new Box(0, 0.7, 0.6, 0.3, 0.4, 0.9)])), OneImage, FaceQ);
    }

    [Fact]
    public void Validate_reports_box_confidence_out_of_range()
    {
        var v = AssertViolations(1, Of(new BoxAnswer("face", [new Box(0, 0.1, 0.1, 0.2, 0.2, -0.1)])), OneImage, FaceQ);

        Assert.Contains("face", v[0]);
    }

    [Fact]
    public void Validate_accepts_zero_boxes()
    {
        AssertViolations(0, Of(new BoxAnswer("face", [])), OneImage, FaceQ);
    }

    [Fact]
    public void Validate_ignores_answers_to_questions_not_asked()
    {
        AssertViolations(0, Of(Team(), new YesNoAnswer("unasked", 7)), Text, TeamQ);
    }
}
