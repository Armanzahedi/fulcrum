using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Fulcrum.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Fulcrum;

public sealed class FulcrumBuilder
{
    internal FulcrumBuilder(IServiceCollection services) => Services = services;

    internal IServiceCollection Services { get; }

    internal bool HasProvider { get; private set; }

    public FulcrumBuilder AddDecision<TDecision>() where TDecision : class, IDecision, new() => Register(() => new TDecision());

    [RequiresUnreferencedCode("Scans the assembly with reflection. Use AddDecision<T>() in trimmed or AOT applications.")]
    public FulcrumBuilder AddDecisionsFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var decisions = assembly.GetTypes().Where(t =>
            t is { IsAbstract: false, IsInterface: false, ContainsGenericParameters: false }
            && typeof(IDecisionInternal).IsAssignableFrom(t)
            && t.GetConstructor(Type.EmptyTypes) is not null);
        foreach (var type in decisions)
            Register(() => (IDecision)Activator.CreateInstance(type)!);
        return this;
    }

    public FulcrumBuilder UseClient<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TClient>()
        where TClient : class, ISystemOne
    {
        SetProvider();
        Services.AddSingleton<ISystemOne, TClient>();
        return this;
    }

    public FulcrumBuilder UseClient(Func<IServiceProvider, ISystemOne> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        SetProvider();
        Services.AddSingleton(factory);
        return this;
    }

    public FulcrumBuilder UseFake(FakeSystemOne fake)
    {
        ArgumentNullException.ThrowIfNull(fake);
        SetProvider();
        Services.AddSingleton<ISystemOne>(fake);
        return this;
    }

    private void SetProvider()
    {
        if (HasProvider)
            throw new FulcrumException("Fulcrum: a provider is already configured; call exactly one of UseFake, UseClient, UseJev or UseOpenAI");
        HasProvider = true;
    }

    private FulcrumBuilder Register(Func<IDecision> create)
    {
        var probe = create();
        if (probe is not IDecisionInternal validated)
            throw new FulcrumException($"{probe.GetType().Name} implements IDecision but does not derive from Decision<TState, TOutcome>");
        validated.EnsureShape();
        validated.RegisterHandlers(Services);
        Services.AddSingleton(probe.GetType(), sp =>
        {
            var decision = create();
            ((IDecisionInternal)decision).Bind(new DecisionRuntime(sp.GetRequiredService<ISystemOne>(), sp));
            return decision;
        });
        return this;
    }
}
