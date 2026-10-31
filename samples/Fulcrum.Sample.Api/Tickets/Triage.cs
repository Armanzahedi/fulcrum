namespace Fulcrum.Sample.Api.Tickets;

public sealed record Ticket(string Id, string Subject, string Body);
public enum Team { Billing, Technical, Sales }
public enum Urgency { CanWait, ThisWeek, Today }
public enum TriageOutcome { AutoReply, PageOnCall, HumanReview }

public sealed class Triage : Decision<Ticket, TriageOutcome>
{
    public static readonly ChoiceQuestion<Team> TeamQ = Question.Choice<Team>("team",
        "Which team should own this ticket?",
        (Team.Billing,   "Payments, refunds, invoices, subscriptions"),
        (Team.Technical, "Bugs, errors, integrations, outages"),
        (Team.Sales,     "Pricing, plans, new accounts"));

    public static readonly YesNoQuestion SafeQ = Question.YesNo("safe_to_automate",
        "Can a standard reply be sent without a human reading it?");

    public static readonly ScoreQuestion<Urgency> UrgencyQ = Question.Score<Urgency>("urgency",
        "How urgently does this need a human?",
        (Urgency.CanWait,  "Can wait a week"),
        (Urgency.ThisWeek, "Needs attention this week"),
        (Urgency.Today,    "Needs attention today"));

    protected override Content Describe(Ticket t) => $"Subject: {t.Subject}\n\n{t.Body}";

    protected override void Define(IRules<TriageOutcome> r)
    {
        r.WhenAnyConfidenceBelow(0.6).Then(TriageOutcome.HumanReview, name: "low-confidence");
        r.When(UrgencyQ.Is(Urgency.Today)).Then(TriageOutcome.PageOnCall);
        r.When(TeamQ.Is(Team.Billing) && SafeQ.Above(0.85)).Then(TriageOutcome.AutoReply);
        r.Default(TriageOutcome.HumanReview);
        r.OnError(TriageOutcome.HumanReview);
    }

    protected override void React(IReactions<Ticket, TriageOutcome> r)
    {
        r.On(TriageOutcome.AutoReply).With<AutoReplyHandler>();
        r.On(TriageOutcome.PageOnCall).With<PageOnCallHandler>();
        r.On(TriageOutcome.HumanReview).With<QueueForReviewHandler>();
    }
}
