using Fulcrum.Testing;
using Microsoft.Extensions.DependencyInjection;
using static Fulcrum.Tests.Triage;

namespace Fulcrum.Tests.DependencyInjection;

public abstract class AbstractTriage : Decision<Ticket, TriageOutcome>
{
    protected override Content Describe(Ticket t) => t.Body;
}

// Generic so that AddDecisionsFromAssembly skips it; only AddDecision<T> registers it.
public sealed class WithoutDefault<TTag> : Decision<Ticket, TriageOutcome>
{
    protected override Content Describe(Ticket t) => t.Body;

    protected override void Define(IRules<TriageOutcome> r) => r.When(SafeQ.Above(0.5)).Then(TriageOutcome.AutoReply);
}

public class AddFulcrumTests
{
    private static readonly Ticket ticket = new("T-1", "Refund", "Please refund order 123");

    private static FakeSystemOne Confident() => new FakeSystemOne()
        .Answer(TeamQ, Team.Billing, 0.9)
        .Answer(SafeQ, 0.95)
        .Answer(UrgencyQ, Urgency.CanWait, 0.8);

    [Fact]
    public async Task AddDecision_registers_singleton_bound_to_provider()
    {
        var fake = Confident();
        using var sp = new ServiceCollection().AddFulcrum(f => f.AddDecision<Triage>().UseFake(fake)).BuildServiceProvider();

        var triage = sp.GetRequiredService<Triage>();
        var result = await triage.DecideAsync(ticket, TestContext.Current.CancellationToken);

        Assert.Same(triage, sp.GetRequiredService<Triage>());
        Assert.Equal(TriageOutcome.AutoReply, result.Outcome);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public void AddDecisionsFromAssembly_finds_Triage_and_IdCheck_and_skips_abstract()
    {
        var services = new ServiceCollection().AddFulcrum(f => f.AddDecisionsFromAssembly(typeof(Triage).Assembly).UseFake(new FakeSystemOne()));

        Assert.Contains(services, d => d.ServiceType == typeof(Triage) && d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, d => d.ServiceType == typeof(IdCheck) && d.Lifetime == ServiceLifetime.Singleton);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(AbstractTriage));
        Assert.DoesNotContain(services, d => d.ServiceType.IsGenericType && d.ServiceType.GetGenericTypeDefinition() == typeof(WithoutDefault<>));
    }

    [Fact]
    public void Handler_types_are_registered_transient()
    {
        var services = new ServiceCollection().AddFulcrum(f => f.AddDecision<Triage>().AddDecision<IdCheck>().UseFake(new FakeSystemOne()));

        foreach (var handler in new[] { typeof(AutoReplyHandler), typeof(PageOnCallHandler), typeof(QueueForReviewHandler), typeof(AskForNewPhotoHandler), typeof(KycQueueHandler) })
            Assert.Equal(ServiceLifetime.Transient, Assert.Single(services, d => d.ServiceType == handler).Lifetime);
    }

    [Fact]
    public void Missing_provider_throws_FulcrumException_from_AddFulcrum()
    {
        var ex = Assert.Throws<FulcrumException>(() => new ServiceCollection().AddFulcrum(f => f.AddDecision<Triage>()));

        Assert.Contains("no provider configured", ex.Message);
    }

    [Fact]
    public void Invalid_decision_throws_from_AddFulcrum_not_first_call()
    {
        var ex = Assert.Throws<FulcrumException>(() => new ServiceCollection().AddFulcrum(f => f.AddDecision<WithoutDefault<int>>().UseFake(new FakeSystemOne())));

        Assert.Contains("no Default", ex.Message);
    }

    [Fact]
    public async Task UseClient_factory_is_used()
    {
        var fake = Confident();
        using var sp = new ServiceCollection().AddFulcrum(f => f.AddDecision<Triage>().UseClient(_ => fake)).BuildServiceProvider();

        await sp.GetRequiredService<Triage>().DecideAsync(ticket, TestContext.Current.CancellationToken);

        Assert.Same(fake, sp.GetRequiredService<ISystemOne>());
        Assert.Single(fake.Requests);
    }

    [Fact]
    public void UseClient_type_is_registered_as_singleton()
    {
        using var sp = new ServiceCollection().AddFulcrum(f => f.UseClient<FakeSystemOne>()).BuildServiceProvider();

        Assert.IsType<FakeSystemOne>(sp.GetRequiredService<ISystemOne>());
        Assert.Same(sp.GetRequiredService<ISystemOne>(), sp.GetRequiredService<ISystemOne>());
    }

    [Fact]
    public async Task Each_service_provider_gets_its_own_decision_bound_to_its_own_model()
    {
        var services = new ServiceCollection().AddSingleton<CallLog>();
        var first = Confident();
        var second = Confident();
        _ = services.AddFulcrum(f => f.AddDecision<Triage>().UseClient(sp => sp.GetRequiredService<FakeHolder>().Fake));
        using var sp1 = services.AddSingleton(new FakeHolder(first)).BuildServiceProvider();
        using var sp2 = services.AddSingleton(new FakeHolder(second)).BuildServiceProvider();

        var d1 = sp1.GetRequiredService<Triage>();
        var d2 = sp2.GetRequiredService<Triage>();
        await d1.DecideAsync(ticket, TestContext.Current.CancellationToken);

        Assert.NotSame(d1, d2);
        Assert.Single(first.Requests);
        Assert.Empty(second.Requests);
    }

    [Fact]
    public void Configuring_a_second_provider_throws_FulcrumException()
    {
        var ex = Assert.Throws<FulcrumException>(() => new ServiceCollection().AddFulcrum(f => f.UseFake(new FakeSystemOne()).UseClient<FakeSystemOne>()));

        Assert.Contains("provider is already configured", ex.Message);
    }

    [Fact]
    public void Calling_AddFulcrum_twice_on_one_collection_throws_FulcrumException()
    {
        var services = new ServiceCollection().AddFulcrum(f => f.AddDecision<Triage>().UseFake(new FakeSystemOne()));

        var ex = Assert.Throws<FulcrumException>(() => services.AddFulcrum(f => f.UseFake(new FakeSystemOne())));

        Assert.Contains("AddFulcrum was already called", ex.Message);
    }

    private sealed record FakeHolder(FakeSystemOne Fake);

    [Fact]
    public void AddFulcrum_can_be_retried_after_a_failed_configure()
    {
        var services = new ServiceCollection();
        Assert.Throws<FulcrumException>(() => services.AddFulcrum(f => f.AddDecision<Triage>()));

        services.AddFulcrum(f => f.AddDecision<Triage>().UseFake(Confident()));

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<Triage>());
    }
}
