namespace Fulcrum;

internal sealed record LeafTrace(string Text, string Observed, bool Satisfied);

internal sealed record RuleTrace(string Name, string ConditionText, string Outcome, RuleStatus Status, IReadOnlyList<LeafTrace> Leaves, string? Observed);

internal enum RuleStatus { Matched, NotMatched, NotEvaluated }
