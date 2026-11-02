namespace Fulcrum.Sample.Transactions.Authorizations;

public sealed class ApproveHandler : IOutcomeHandler<Authorization, AuthOutcome>
{
    public Task HandleAsync(Authorization a, DecisionResult<AuthOutcome> result, CancellationToken ct)
    {
        Console.WriteLine($"[approve] {a.TransactionId}: {a.Amount} {a.Currency} at {a.MerchantName}");
        return Task.CompletedTask;
    }
}

public sealed class ThreeDSecureChallengeHandler : IOutcomeHandler<Authorization, AuthOutcome>
{
    public Task HandleAsync(Authorization a, DecisionResult<AuthOutcome> result, CancellationToken ct)
    {
        Console.WriteLine($"[step-up] {a.TransactionId}: 3-D Secure challenge requested, reason {result.MatchedRule ?? "model unavailable"}");
        return Task.CompletedTask;
    }
}

public sealed class DeclineHandler : IOutcomeHandler<Authorization, AuthOutcome>
{
    public Task HandleAsync(Authorization a, DecisionResult<AuthOutcome> result, CancellationToken ct)
    {
        Console.WriteLine($"[decline] {a.TransactionId}: declined as suspected fraud; cardholder {a.CardId} notified");
        return Task.CompletedTask;
    }
}

public sealed class AnalystQueueHandler : IOutcomeHandler<Authorization, AuthOutcome>
{
    public Task HandleAsync(Authorization a, DecisionResult<AuthOutcome> result, CancellationToken ct)
    {
        Console.WriteLine($"[review] {a.TransactionId}: queued for an analyst with the evidence below");
        return Task.CompletedTask;
    }
}
