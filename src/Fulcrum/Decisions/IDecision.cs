using Microsoft.Extensions.DependencyInjection;

namespace Fulcrum;

public interface IDecision
{
}

internal interface IDecisionInternal
{
    string Name { get; }

    void EnsureShape();

    void RegisterHandlers(IServiceCollection services);

    void Bind(DecisionRuntime runtime);
}
