
namespace Fulcrum.Sample.Api.Tickets;

public static class TicketsEndpoints
{
    public static IEndpointRouteBuilder MapTickets(this IEndpointRouteBuilder app)
    {
        app.MapPost("/tickets", async (Ticket ticket, Triage triage, CancellationToken ct) =>
        {
            var result = await triage.RunAsync(ticket, ct);
            return Results.Ok(new { outcome = result.Outcome.ToString(), result.MatchedRule, result.Confidence, result.IsFallback });
        });
        return app;
    }
}
