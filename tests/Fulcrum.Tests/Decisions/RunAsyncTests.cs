using Fulcrum.Testing;
using Microsoft.Extensions.DependencyInjection;
using static Fulcrum.Tests.Triage;
using static Fulcrum.Tests.IdCheck;

namespace Fulcrum.Tests.Decisions;

public interface IMissing;

public sealed class NeedsMissingHandler(IMissing missing) : IOutcomeHandler<Ticket, TriageOutcome>
{
    public Task HandleAsync(Ticket state, DecisionResult<TriageOutcome> result, CancellationToken cancellationToken) => Task.FromResult(missing);
}

public sealed class ThrowingHandler : IOutcomeHandler<Ticket, TriageOutcome>
{
    public Task HandleAsync(Ticket state, DecisionResult<TriageOutcome> result, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("handler failed");
}

public abstract class SafeTriage : Decision<Ticket, TriageOutcome>
{
    protected override Content Describe(Ticket t) => t.Body;

    protected override void Define(IRules<TriageOutcome> r)
    {
        r.When(SafeQ.Above(0.85)).Then(TriageOutcome.AutoReply);
        r.Default(TriageOutcome.HumanReview);
    }
}

public sealed class DelegateTriage : SafeTriage
{
    private readonly List<TriageOutcome> _seen = [];

    public IReadOnlyList<TriageOutcome> Seen => _seen;

    protected override void React(IReactions<Ticket, TriageOutcome> r)
    {
        foreach (var outcome in Enum.GetValues<TriageOutcome>())
            r.On(outcome).With((_, result, _) => { _seen.Add(result.Outcome); return Task.CompletedTask; });
    }
}

public sealed class ThrowingTriage : SafeTriage
{
    protected override void React(IReactions<Ticket, TriageOutcome> r)
    {
        r.On(TriageOutcome.AutoReply).With<ThrowingHandler>();
        r.On(TriageOutcome.PageOnCall).Nothing();
        r.On(TriageOutcome.HumanReview).Nothing();
    }
}

public sealed class NeedsMissingTriage : SafeTriage
{
    protected override void React(IReactions<Ticket, TriageOutcome> r)
    {
        r.On(TriageOutcome.AutoReply).With<NeedsMissingHandler>();
        r.On(TriageOutcome.PageOnCall).Nothing();
        r.On(TriageOutcome.HumanReview).Nothing();
    }
}

public sealed class NoReactions : SafeTriage;

public sealed class ScopedDependency : IDisposable
{
    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}

public sealed class ScopedSeen
{
    public ScopedDependency? Dependency { get; set; }
}

public sealed class ScopedHandler(ScopedDependency dependency, ScopedSeen seen) : IOutcomeHandler<Ticket, TriageOutcome>
{
    public Task HandleAsync(Ticket state, DecisionResult<TriageOutcome> result, CancellationToken cancellationToken)
    {
        seen.Dependency = dependency;
        return Task.CompletedTask;
    }
}

public sealed class ScopedTriage : SafeTriage
{
    protected override void React(IReactions<Ticket, TriageOutcome> r)
    {
        r.On(TriageOutcome.AutoReply).With<ScopedHandler>();
        r.On(TriageOutcome.PageOnCall).Nothing();
        r.On(TriageOutcome.HumanReview).Nothing();
    }
}

public class RunAsyncTests
{
    private static readonly Ticket ticket = new("T-1", "Refund", "Please refund order 123");
    private static readonly IdDocument document = new("A-1", [1, 2, 3], "image/png", "Passport");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServiceProvider Provider(FakeSystemOne fake, Action<FulcrumBuilder> add) =>
        new ServiceCollection().AddSingleton<CallLog>().AddFulcrum(f => add(f.UseFake(fake))).BuildServiceProvider();

    private static FakeSystemOne Confident() => new FakeSystemOne()
        .Answer(TeamQ, Team.Billing, 0.9)
        .Answer(SafeQ, 0.95)
        .Answer(UrgencyQ, Urgency.CanWait, 0.8);

    [Fact]
    public async Task RunAsync_dispatches_to_the_handler_for_the_outcome()
    {
        using var sp = Provider(Confident(), f => f.AddDecision<Triage>());

        var result = await sp.GetRequiredService<Triage>().RunAsync(ticket, Ct);

        var call = Assert.Single(sp.GetRequiredService<CallLog>().Calls);
        Assert.Equal("AutoReply", call.Handler);
        Assert.Same(ticket, call.State);
        Assert.Same(result, call.Result);
    }

    [Fact]
    public async Task RunAsync_Nothing_dispatches_nothing()
    {
        var fake = new FakeSystemOne()
            .Answer(FaceQ, new Box(0, 0.1, 0.1, 0.2, 0.2, 0.9))
            .Answer(KindQ, DocumentKind.Passport, 0.9)
            .Answer(TamperedQ, 0.05);
        using var sp = Provider(fake, f => f.AddDecision<IdCheck>());

        var result = await sp.GetRequiredService<IdCheck>().RunAsync(document, Ct);

        Assert.Equal(IdCheckOutcome.Accept, result.Outcome);
        Assert.Empty(sp.GetRequiredService<CallLog>().Calls);
    }

    [Fact]
    public async Task RunAsync_delegate_binding_is_invoked()
    {
        using var sp = Provider(new FakeSystemOne().Answer(SafeQ, 0.95), f => f.AddDecision<DelegateTriage>());
        var decision = sp.GetRequiredService<DelegateTriage>();

        await decision.RunAsync(ticket, Ct);

        Assert.Equal([TriageOutcome.AutoReply], decision.Seen);
    }

    [Fact]
    public async Task RunAsync_fallback_outcome_still_dispatches()
    {
        using var sp = Provider(new FakeSystemOne().Fail(new SystemOneException("503")), f => f.AddDecision<Triage>());

        var result = await sp.GetRequiredService<Triage>().RunAsync(ticket, Ct);

        Assert.True(result.IsFallback);
        Assert.Equal("QueueForReview", Assert.Single(sp.GetRequiredService<CallLog>().Calls).Handler);
    }

    [Fact]
    public async Task RunAsync_handler_exception_propagates()
    {
        using var sp = Provider(new FakeSystemOne().Answer(SafeQ, 0.95), f => f.AddDecision<ThrowingTriage>());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sp.GetRequiredService<ThrowingTriage>().RunAsync(ticket, Ct));

        Assert.Equal("handler failed", ex.Message);
    }

    [Fact]
    public async Task RunAsync_without_reactions_throws_FulcrumException()
    {
        var fake = new FakeSystemOne().Answer(SafeQ, 0.95);
        using var sp = Provider(fake, f => f.AddDecision<NoReactions>());

        var ex = await Assert.ThrowsAsync<FulcrumException>(() => sp.GetRequiredService<NoReactions>().RunAsync(ticket, Ct));

        Assert.Contains("no reactions declared", ex.Message);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task RunAsync_resolves_the_handler_in_a_scope_that_is_disposed_afterwards()
    {
        var seen = new ScopedSeen();
        using var sp = new ServiceCollection()
            .AddSingleton(seen)
            .AddScoped<ScopedDependency>()
            .AddFulcrum(f => f.UseFake(new FakeSystemOne().Answer(SafeQ, 0.95)).AddDecision<ScopedTriage>())
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await sp.GetRequiredService<ScopedTriage>().RunAsync(ticket, Ct);

        Assert.True(seen.Dependency is { Disposed: true });
    }

    [Fact]
    public async Task RunAsync_unbound_throws_FulcrumException()
    {
        await Assert.ThrowsAsync<FulcrumException>(() => new Triage().RunAsync(ticket, Ct));
    }

    [Fact]
    public async Task RunAsync_handler_with_unregistered_dependency_surfaces_container_exception()
    {
        using var sp = Provider(new FakeSystemOne().Answer(SafeQ, 0.95), f => f.AddDecision<NeedsMissingTriage>());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sp.GetRequiredService<NeedsMissingTriage>().RunAsync(ticket, Ct));

        Assert.Contains(nameof(IMissing), ex.Message);
    }
}
