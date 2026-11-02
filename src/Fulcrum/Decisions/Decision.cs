using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Fulcrum.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Fulcrum;

public abstract class Decision<TState, TOutcome> : IDecision, IDecisionInternal where TOutcome : struct, Enum
{
    private readonly Lazy<DecisionShape<TState, TOutcome>> _shape;
    private DecisionRuntime? _runtime;

    protected Decision() =>
        _shape = new(() => DecisionShape<TState, TOutcome>.Build(Name, Define, React), LazyThreadSafetyMode.ExecutionAndPublication);

    public virtual string Name => GetType().Name;

    internal DecisionShape<TState, TOutcome> Shape => _shape.Value;

    protected abstract Content Describe(TState state);

    protected abstract void Define(IRules<TOutcome> rules);

    protected virtual void React(IReactions<TState, TOutcome> reactions)
    {
    }

    [SuppressMessage("ApiDesign", "RS0026", Justification = "Spec §6.4: both overloads take an optional token; the model overload is the test entry point.")]
    public Task<DecisionResult<TOutcome>> DecideAsync(TState state, CancellationToken cancellationToken = default) =>
        DecideAsync(state, Runtime.Model, cancellationToken);

    [SuppressMessage("ApiDesign", "RS0026", Justification = "Spec §6.4: both overloads take an optional token; the model overload is the test entry point.")]
    public async Task<DecisionResult<TOutcome>> DecideAsync(TState state, ISystemOne model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        var shape = Shape;
        var content = Describe(state);
        if (content.Text.Length == 0 && content.Images.Count == 0)
            throw new FulcrumException($"{Name}: Describe returned empty content");
        if (content.Images.Count == 0 && shape.Questions.FirstOrDefault(q => q.Kind == QuestionKind.Box) is { } box)
            throw new FulcrumException($"{Name}: box question '{box.Name}' needs at least one image in the content");

        Answers answers;
        try
        {
            answers = await model.AskAsync(content, shape.Questions, cancellationToken).ConfigureAwait(false);
            var violations = answers.Validate(content, shape.Questions);
            if (violations.Count > 0)
                throw new SystemOneException(violations);
        }
        catch (SystemOneException ex) when (shape.OnError.HasValue)
        {
            return new DecisionResult<TOutcome>(Name, shape.OnError.Value, null, null, Answers.Empty, ex, [], [], null);
        }

        return Evaluate(shape, answers);
    }

    public async Task<DecisionResult<TOutcome>> RunAsync(TState state, CancellationToken cancellationToken = default)
    {
        var runtime = Runtime;
        var shape = Shape;
        if (!shape.HasReactions)
            throw new FulcrumException($"{Name}: no reactions declared; use DecideAsync or override React");

        var result = await DecideAsync(state, runtime.Model, cancellationToken).ConfigureAwait(false);
        switch (shape.Reactions[result.Outcome])
        {
            case HandlerBinding<TState, TOutcome>.TypeBinding binding:
            {
                await using var scope = runtime.Services.CreateAsyncScope();
                var handler = (IOutcomeHandler<TState, TOutcome>)scope.ServiceProvider.GetRequiredService(binding.HandlerType);
                await handler.HandleAsync(state, result, cancellationToken).ConfigureAwait(false);
                break;
            }
            case HandlerBinding<TState, TOutcome>.DelegateBinding binding:
                await binding.Handler(state, result, cancellationToken).ConfigureAwait(false);
                break;
        }
        return result;
    }

    void IDecisionInternal.EnsureShape() => _ = Shape;

    void IDecisionInternal.RegisterHandlers(IServiceCollection services)
    {
        foreach (var binding in Shape.Reactions.Values.OfType<HandlerBinding<TState, TOutcome>.TypeBinding>())
            services.TryAddTransient(binding.HandlerType);
    }

    void IDecisionInternal.Bind(DecisionRuntime runtime) => _runtime = runtime;

    private DecisionRuntime Runtime => _runtime ?? throw new FulcrumException(
        $"{Name}: not bound to a provider; register it with AddFulcrum, or call DecideAsync(state, model) in tests");

    private DecisionResult<TOutcome> Evaluate(DecisionShape<TState, TOutcome> shape, Answers answers)
    {
        var traces = new List<RuleTrace>(shape.Rules.Count);
        Rule<TOutcome>? matched = null;
        var askedMin = MinConfidence(answers, shape.Questions);

        foreach (var rule in shape.Rules)
        {
            var outcome = NameOf(rule.Outcome);
            if (matched is not null)
            {
                traces.Add(new RuleTrace(rule.Name, ConditionText(rule), outcome, RuleStatus.NotEvaluated, [], null));
                continue;
            }

            bool satisfied;
            if (rule.Condition is null)
            {
                satisfied = askedMin < rule.AnyConfidenceBelow;
                var observed = askedMin is { } min ? $"min {ConditionEvaluator.Format(min)}" : "n/a";
                traces.Add(new RuleTrace(rule.Name, ConditionText(rule), outcome, Status(satisfied), [], observed));
            }
            else
            {
                (satisfied, var leaves) = ConditionEvaluator.Evaluate(rule.Condition, answers);
                traces.Add(new RuleTrace(rule.Name, ConditionText(rule), outcome, Status(satisfied), leaves, leaves.Count == 1 ? leaves[0].Observed : null));
            }

            if (satisfied)
                matched = rule;
        }

        var confidence = matched?.Condition is { } condition
            ? MinConfidence(answers, ConditionEvaluator.ReferencedQuestions(condition))
            : askedMin;

        return new DecisionResult<TOutcome>(
            Name,
            matched?.Outcome ?? shape.Default,
            matched?.Name ?? "default",
            confidence,
            answers,
            null,
            traces,
            shape.Questions,
            NameOf(shape.Default));
    }

    private static double? MinConfidence(Answers answers, IEnumerable<Question> questions) =>
        questions.Select(q => ConditionEvaluator.ConfidenceOf(answers.Raw[q.Name])).Min();

    private static RuleStatus Status(bool satisfied) => satisfied ? RuleStatus.Matched : RuleStatus.NotMatched;

    private static string ConditionText(Rule<TOutcome> rule) =>
        rule.Condition?.ToString() ?? $"any confidence < {rule.AnyConfidenceBelow!.Value.ToString(CultureInfo.InvariantCulture)}";

    private static string NameOf(TOutcome outcome) => Enum.GetName(outcome) ?? outcome.ToString();
}
