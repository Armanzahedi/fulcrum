using Fulcrum;

namespace Fulcrum.Tests;

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

public sealed record IdDocument(string ApplicantId, byte[] Photo, string MediaType, string DeclaredType);
public enum DocumentKind { Passport, NationalId, DrivingLicence, Other }
public enum IdCheckOutcome { Accept, Retake, ManualReview }

public sealed class IdCheck : Decision<IdDocument, IdCheckOutcome>
{
    public static readonly BoxQuestion FaceQ = Question.Box("face",
        "Where is the holder's face photo on the document?");

    public static readonly ChoiceQuestion<DocumentKind> KindQ = Question.Choice<DocumentKind>("kind",
        "What kind of identity document is this?",
        (DocumentKind.Passport,       "Passport data page"),
        (DocumentKind.NationalId,     "National identity card"),
        (DocumentKind.DrivingLicence, "Driving licence"),
        (DocumentKind.Other,          "Anything else"));

    public static readonly YesNoQuestion TamperedQ = Question.YesNo("tampered",
        "Does the document show signs of tampering or editing?");

    protected override Content Describe(IdDocument d) =>
        Content.From($"Applicant declared document type: {d.DeclaredType}")
               .WithImage(d.Photo, d.MediaType);

    protected override void Define(IRules<IdCheckOutcome> r)
    {
        r.When(FaceQ.None() || FaceQ.Count().Above(1)).Then(IdCheckOutcome.Retake, name: "no-single-face");
        r.When(TamperedQ.Above(0.5)).Then(IdCheckOutcome.ManualReview, name: "possible-tampering");
        r.When(KindQ.Is(DocumentKind.Other)).Then(IdCheckOutcome.ManualReview, name: "unknown-document");
        r.WhenAnyConfidenceBelow(0.7).Then(IdCheckOutcome.ManualReview, name: "low-confidence");
        r.Default(IdCheckOutcome.Accept);
        r.OnError(IdCheckOutcome.ManualReview);
    }

    protected override void React(IReactions<IdDocument, IdCheckOutcome> r)
    {
        r.On(IdCheckOutcome.Accept).Nothing();
        r.On(IdCheckOutcome.Retake).With<AskForNewPhotoHandler>();
        r.On(IdCheckOutcome.ManualReview).With<KycQueueHandler>();
    }
}

public sealed record HandlerCall(string Handler, object State, object Result);

public sealed class CallLog
{
    private readonly Lock _lock = new();
    private readonly List<HandlerCall> _calls = [];

    public IReadOnlyList<HandlerCall> Calls
    {
        get
        {
            lock (_lock)
                return [.. _calls];
        }
    }

    public Task Record(string handler, object state, object result)
    {
        lock (_lock)
            _calls.Add(new HandlerCall(handler, state, result));
        return Task.CompletedTask;
    }
}

public sealed class AutoReplyHandler(CallLog log) : IOutcomeHandler<Ticket, TriageOutcome>
{
    public Task HandleAsync(Ticket state, DecisionResult<TriageOutcome> result, CancellationToken cancellationToken) => log.Record("AutoReply", state, result);
}

public sealed class PageOnCallHandler(CallLog log) : IOutcomeHandler<Ticket, TriageOutcome>
{
    public Task HandleAsync(Ticket state, DecisionResult<TriageOutcome> result, CancellationToken cancellationToken) => log.Record("PageOnCall", state, result);
}

public sealed class QueueForReviewHandler(CallLog log) : IOutcomeHandler<Ticket, TriageOutcome>
{
    public Task HandleAsync(Ticket state, DecisionResult<TriageOutcome> result, CancellationToken cancellationToken) => log.Record("QueueForReview", state, result);
}

public sealed class AskForNewPhotoHandler(CallLog log) : IOutcomeHandler<IdDocument, IdCheckOutcome>
{
    public Task HandleAsync(IdDocument state, DecisionResult<IdCheckOutcome> result, CancellationToken cancellationToken) => log.Record("AskForNewPhoto", state, result);
}

public sealed class KycQueueHandler(CallLog log) : IOutcomeHandler<IdDocument, IdCheckOutcome>
{
    public Task HandleAsync(IdDocument state, DecisionResult<IdCheckOutcome> result, CancellationToken cancellationToken) => log.Record("KycQueue", state, result);
}
