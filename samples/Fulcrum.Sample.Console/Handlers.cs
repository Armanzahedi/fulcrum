namespace Fulcrum.Sample.Tickets;

public sealed class AutoReplyHandler : IOutcomeHandler<Ticket, TriageOutcome>
{
    public Task HandleAsync(Ticket ticket, DecisionResult<TriageOutcome> result, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[auto-reply] sending the standard {result.Answers.Get(Triage.TeamQ).Value} reply to {ticket.Id}");
        return Task.CompletedTask;
    }
}

public sealed class PageOnCallHandler : IOutcomeHandler<Ticket, TriageOutcome>
{
    public Task HandleAsync(Ticket ticket, DecisionResult<TriageOutcome> result, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[page] paging on-call about {ticket.Id}");
        return Task.CompletedTask;
    }
}

public sealed class QueueForReviewHandler : IOutcomeHandler<Ticket, TriageOutcome>
{
    public Task HandleAsync(Ticket ticket, DecisionResult<TriageOutcome> result, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[review] queueing {ticket.Id} for a human");
        return Task.CompletedTask;
    }
}
