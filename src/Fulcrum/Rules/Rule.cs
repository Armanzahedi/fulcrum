namespace Fulcrum;

internal sealed record Rule<TOutcome>(string Name, Condition? Condition, double? AnyConfidenceBelow, TOutcome Outcome);
