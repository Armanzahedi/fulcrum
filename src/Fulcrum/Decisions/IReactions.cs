using System.Diagnostics.CodeAnalysis;

namespace Fulcrum;

public interface IReactions<TState, TOutcome> where TOutcome : struct, Enum
{
    IReaction<TState, TOutcome> On(TOutcome outcome);
}

public interface IReaction<TState, TOutcome> where TOutcome : struct, Enum
{
    void With<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>()
        where THandler : class, IOutcomeHandler<TState, TOutcome>;

    void With(Func<TState, DecisionResult<TOutcome>, CancellationToken, Task> handler);

    void Nothing();
}

public interface IOutcomeHandler<TState, TOutcome> where TOutcome : struct, Enum
{
    Task HandleAsync(TState state, DecisionResult<TOutcome> result, CancellationToken cancellationToken);
}
