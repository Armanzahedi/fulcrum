using System.Diagnostics.CodeAnalysis;

namespace Fulcrum;

internal sealed class DecisionShape<TState, TOutcome> where TOutcome : struct, Enum
{
    internal DecisionShape(
        IReadOnlyList<Rule<TOutcome>> rules,
        TOutcome @default,
        TOutcome? onError,
        IReadOnlyList<Question> questions,
        IReadOnlyDictionary<TOutcome, HandlerBinding<TState, TOutcome>> reactions)
    {
        Rules = rules;
        Default = @default;
        OnError = onError;
        Questions = questions;
        Reactions = reactions;
    }

    public IReadOnlyList<Rule<TOutcome>> Rules { get; }

    public TOutcome Default { get; }

    public TOutcome? OnError { get; }

    public IReadOnlyList<Question> Questions { get; }

    public IReadOnlyDictionary<TOutcome, HandlerBinding<TState, TOutcome>> Reactions { get; }

    public bool HasReactions => Reactions.Count > 0;

    public static DecisionShape<TState, TOutcome> Build(string decisionName, Action<IRules<TOutcome>> define, Action<IReactions<TState, TOutcome>> react)
    {
        var builder = new DecisionShapeBuilder<TState, TOutcome>();
        try
        {
            define(builder);
            react(builder);
        }
        catch (FulcrumException ex)
        {
            throw new FulcrumException($"{decisionName}: {ex.Message}");
        }
        return builder.Build(decisionName);
    }
}

internal sealed class DecisionShapeBuilder<TState, TOutcome> : IRules<TOutcome>, IReactions<TState, TOutcome> where TOutcome : struct, Enum
{
    private readonly List<PendingRule> _rules = [];
    private readonly List<PendingReaction> _reactions = [];
    private readonly List<string> _problems = [];
    private TOutcome? _default;
    private TOutcome? _onError;

    public IRule<TOutcome> When(Condition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return Add(new PendingRule(condition, null));
    }

    public IRule<TOutcome> WhenAnyConfidenceBelow(double threshold) => Add(new PendingRule(null, threshold));

    public void Default(TOutcome outcome)
    {
        if (_default.HasValue)
            _problems.Add("Default declared more than once");
        _default = outcome;
    }

    public void OnError(TOutcome outcome)
    {
        if (_onError.HasValue)
            _problems.Add("OnError declared more than once");
        _onError = outcome;
    }

    public IReaction<TState, TOutcome> On(TOutcome outcome)
    {
        var reaction = new PendingReaction(outcome);
        _reactions.Add(reaction);
        return reaction;
    }

    public DecisionShape<TState, TOutcome> Build(string decisionName)
    {
        var problems = new List<string>(_problems);
        if (_rules.Count == 0)
            problems.Add("no rules declared");
        if (!_default.HasValue)
            problems.Add("no Default declared");
        if (_rules.Any(r => r.Rule is null))
            problems.Add("When without Then");

        var rules = _rules.Select(r => r.Rule).OfType<Rule<TOutcome>>().ToList();
        foreach (var name in rules.GroupBy(r => r.Name).Where(g => g.Count() > 1).Select(g => g.Key))
            problems.Add($"duplicate rule name '{name}'");
        if (rules.Any(r => r.Name == "default"))
            problems.Add("rule name 'default' is reserved");
        foreach (var rule in rules.Where(r => r.AnyConfidenceBelow is { } t && !IsUnit(t)))
            problems.Add($"rule '{rule.Name}' threshold {rule.AnyConfidenceBelow} is outside [0, 1]");

        var leaves = rules.Where(r => r.Condition is not null).SelectMany(r => ConditionEvaluator.Leaves(r.Condition!)).ToList();
        var questions = leaves.Select(l => l.Question).Distinct(ReferenceEqualityComparer.Instance).Cast<Question>().ToList();
        foreach (var name in questions.GroupBy(q => q.Name).Where(g => g.Count() > 1).Select(g => g.Key))
            problems.Add($"two different questions are named '{name}'");
        if (rules.Count > 0 && questions.Count == 0)
            problems.Add("no rule references a question");
        problems.AddRange(questions.SelectMany(q => q.Problems()));
        problems.AddRange(leaves.Select(LeafProblem).OfType<string>().Distinct());

        var outcomes = rules.Select(r => (TOutcome?)r.Outcome).Append(_default).Append(_onError).OfType<TOutcome>().Concat(_reactions.Select(r => r.Outcome));
        foreach (var outcome in outcomes.Where(o => !Enum.IsDefined(o)).Distinct())
            problems.Add($"{outcome} is not a defined {typeof(TOutcome).Name}");

        var mapped = _reactions.Where(r => r.Binding is not null).ToList();
        if (mapped.Count > 0)
        {
            foreach (var outcome in mapped.GroupBy(r => r.Outcome).Where(g => g.Count() > 1).Select(g => g.Key))
                problems.Add($"React maps {outcome} more than once");
            foreach (var outcome in Enum.GetValues<TOutcome>().Where(o => !mapped.Any(r => r.Outcome.Equals(o))))
                problems.Add($"React does not map {outcome}");
        }

        if (problems.Count > 0)
            throw new FulcrumException($"{decisionName}: {string.Join("; ", problems)}");

        return new DecisionShape<TState, TOutcome>(
            rules,
            _default!.Value,
            _onError,
            questions,
            mapped.ToDictionary(r => r.Outcome, r => r.Binding!));
    }

    private static string? LeafProblem(Leaf leaf) => leaf switch
    {
        ChoiceIs l when !l.Question.Choices.Any(c => c.Name == l.Option) => $"'{l.Question.Name}' has no option {l.Option}",
        ConfidenceAbove l when !IsUnit(l.T) => Outside(l, l.T),
        YesNoAbove l when !IsUnit(l.T) => Outside(l, l.T),
        YesNoBelow l when !IsUnit(l.T) => Outside(l, l.T),
        BoxFoundAbove l when !IsUnit(l.T) => Outside(l, l.T),
        BoxCount l when l.N < 0 => $"'{l.Question.Name}' has a negative count {l.N}",
        _ => null,
    };

    private static string Outside(Leaf leaf, double t) => $"'{leaf}' threshold {t} is outside [0, 1]";

    private static bool IsUnit(double t) => t is >= 0 and <= 1;

    private PendingRule Add(PendingRule rule)
    {
        _rules.Add(rule);
        return rule;
    }

    private sealed class PendingRule(Condition? condition, double? anyConfidenceBelow) : IRule<TOutcome>
    {
        public Rule<TOutcome>? Rule { get; private set; }

        public void Then(TOutcome outcome, string? name = null) =>
            Rule = new(name ?? Enum.GetName(outcome) ?? outcome.ToString(), condition, anyConfidenceBelow, outcome);
    }

    private sealed class PendingReaction(TOutcome outcome) : IReaction<TState, TOutcome>
    {
        public TOutcome Outcome => outcome;

        public HandlerBinding<TState, TOutcome>? Binding { get; private set; }

        public void With<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>()
            where THandler : class, IOutcomeHandler<TState, TOutcome> =>
            Binding = new HandlerBinding<TState, TOutcome>.TypeBinding(typeof(THandler));

        public void With(Func<TState, DecisionResult<TOutcome>, CancellationToken, Task> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            Binding = new HandlerBinding<TState, TOutcome>.DelegateBinding(handler);
        }

        public void Nothing() => Binding = new HandlerBinding<TState, TOutcome>.NothingBinding();
    }
}
