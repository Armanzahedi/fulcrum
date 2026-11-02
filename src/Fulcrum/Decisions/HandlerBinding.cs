using System.Diagnostics.CodeAnalysis;

namespace Fulcrum;

internal abstract record HandlerBinding<TState, TOutcome> where TOutcome : struct, Enum
{
    internal sealed record TypeBinding : HandlerBinding<TState, TOutcome>
    {
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        private readonly Type _handlerType;

        public TypeBinding([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type handlerType) => _handlerType = handlerType;

        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        public Type HandlerType => _handlerType;
    }

    internal sealed record DelegateBinding(Func<TState, DecisionResult<TOutcome>, CancellationToken, Task> Handler) : HandlerBinding<TState, TOutcome>;

    internal sealed record NothingBinding : HandlerBinding<TState, TOutcome>;
}
