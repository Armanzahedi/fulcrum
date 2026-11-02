namespace Fulcrum.Sample.Api.Tickets;

public sealed class AutoReplyHandler(ILogger<AutoReplyHandler> logger) : IOutcomeHandler<Ticket, TriageOutcome>
{
    public Task HandleAsync(Ticket state, DecisionResult<TriageOutcome> result, CancellationToken cancellationToken)
    {
        logger.LogInformation("Sending the standard {Team} reply to {Ticket}", result.Answers.Get(Triage.TeamQ).Value, state.Id);
        return Task.CompletedTask;
    }
}

public sealed class PageOnCallHandler(ILogger<PageOnCallHandler> logger) : IOutcomeHandler<Ticket, TriageOutcome>
{
    public Task HandleAsync(Ticket state, DecisionResult<TriageOutcome> result, CancellationToken cancellationToken)
    {
        logger.LogInformation("Paging on-call about {Ticket}:\n{Explanation}", state.Id, result.Explain());
        return Task.CompletedTask;
    }
}

public sealed class QueueForReviewHandler(ILogger<QueueForReviewHandler> logger) : IOutcomeHandler<Ticket, TriageOutcome>
{
    public Task HandleAsync(Ticket state, DecisionResult<TriageOutcome> result, CancellationToken cancellationToken)
    {
        logger.LogInformation("Queueing {Ticket} for human review (fallback: {IsFallback})", state.Id, result.IsFallback);
        return Task.CompletedTask;
    }
}
