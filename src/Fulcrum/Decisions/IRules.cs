namespace Fulcrum;

public interface IRules<TOutcome> where TOutcome : struct, Enum
{
    IRule<TOutcome> When(Condition condition);

    IRule<TOutcome> WhenAnyConfidenceBelow(double threshold);

    void Default(TOutcome outcome);

    void OnError(TOutcome outcome);
}

public interface IRule<TOutcome> where TOutcome : struct, Enum
{
    void Then(TOutcome outcome, string? name = null);
}
