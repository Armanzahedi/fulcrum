using Fulcrum.OpenAI;
using Microsoft.Extensions.DependencyInjection;

namespace Fulcrum.Tests;

public class LiveOpenRouterTests
{
    private static readonly string? Key = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
    private static readonly string Model = Environment.GetEnvironmentVariable("OPENROUTER_MODEL") ?? "typesafe/jev-router";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServiceProvider Provider() => new ServiceCollection()
        .AddSingleton<CallLog>()
        .AddFulcrum(f => f
            .AddDecision<Triage>()
            .AddDecision<IdCheck>()
            .UseOpenAI(o =>
            {
                o.ApiKey = Key;
                o.Model = Model;
                o.BaseAddress = new Uri("https://openrouter.ai/api/v1");
            }))
        .BuildServiceProvider();

    [Fact]
    public async Task Billing_refund_ticket_is_answered_and_routed_to_billing()
    {
        Assert.SkipWhen(string.IsNullOrWhiteSpace(Key), "OPENROUTER_API_KEY is not set");
        using var sp = Provider();

        var result = await sp.GetRequiredService<Triage>().RunAsync(
            new Ticket("T-1", "Refund for double charge", "I was charged twice for my March invoice. Please refund the duplicate payment."), Ct);

        TestContext.Current.TestOutputHelper?.WriteLine(result.Explain());
        Assert.False(result.IsFallback, result.Error?.ToString());
        Assert.Equal(Team.Billing, result.Answers.Get(Triage.TeamQ).Value);
        Assert.Single(sp.GetRequiredService<CallLog>().Calls);
    }

    [Fact]
    public async Task Production_outage_is_urgent_and_technical()
    {
        Assert.SkipWhen(string.IsNullOrWhiteSpace(Key), "OPENROUTER_API_KEY is not set");
        using var sp = Provider();

        var result = await sp.GetRequiredService<Triage>().DecideAsync(
            new Ticket("T-2", "Production is down", "Since 09:00 every API call returns HTTP 500 and none of our customers can log in. This is blocking all of our users right now."), Ct);

        TestContext.Current.TestOutputHelper?.WriteLine(result.Explain());
        Assert.False(result.IsFallback, result.Error?.ToString());
        Assert.Equal(Team.Technical, result.Answers.Get(Triage.TeamQ).Value);
        Assert.Equal(TriageOutcome.PageOnCall, result.Outcome);
    }

    [Fact]
    public async Task Id_document_image_is_answered_with_valid_boxes()
    {
        Assert.SkipWhen(string.IsNullOrWhiteSpace(Key), "OPENROUTER_API_KEY is not set");
        using var sp = Provider();
        var photo = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "passport-specimen.png"), Ct);

        var result = await sp.GetRequiredService<IdCheck>().DecideAsync(new IdDocument("A-1", photo, "image/png", "Passport"), Ct);

        TestContext.Current.TestOutputHelper?.WriteLine(result.Explain());
        Assert.False(result.IsFallback, result.Error?.ToString());
        Assert.Equal(DocumentKind.Passport, result.Answers.Get(IdCheck.KindQ).Value);
        Assert.NotEmpty(result.Answers.Get(IdCheck.FaceQ).Boxes);
    }
}
