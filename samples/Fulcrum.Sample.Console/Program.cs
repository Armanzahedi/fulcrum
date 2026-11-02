using System.Globalization;
using System.Text;
using Fulcrum;
using Fulcrum.Sample.Tickets;
using Fulcrum.Testing;
using Microsoft.Extensions.DependencyInjection;

Console.OutputEncoding = Encoding.UTF8;

var openRouterKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
var jevKey = Environment.GetEnvironmentVariable("JEV_API_KEY");

using var services = new ServiceCollection()
    .AddFulcrum(f =>
    {
        f.AddDecision<Triage>();
        if (!string.IsNullOrWhiteSpace(openRouterKey))
        {
            f.UseOpenAI(o =>
            {
                o.ApiKey = openRouterKey;
                o.BaseAddress = new Uri("https://openrouter.ai/api/v1");
                o.Model = Environment.GetEnvironmentVariable("OPENROUTER_MODEL") ?? "typesafe/jev-router";
            });
        }
        else if (!string.IsNullOrWhiteSpace(jevKey))
        {
            f.UseJev(o => o.ApiKey = jevKey);
        }
        else
        {
            f.UseFake(new FakeSystemOne()
                .Answer(Triage.TeamQ, Team.Billing, confidence: 0.90)
                .Answer(Triage.SafeQ, 0.95)
                .Answer(Triage.UrgencyQ, Urgency.CanWait, confidence: 0.80));
        }
    })
    .BuildServiceProvider();

var subject = args.Length > 0 ? args[0] : "Refund";
var body = args.Length > 1 ? args[1] : "Please refund order 123";
var result = await services.GetRequiredService<Triage>().RunAsync(new Ticket("T-1", subject, body));

Console.WriteLine(result.Outcome);
Console.WriteLine(result.Confidence?.ToString(CultureInfo.InvariantCulture) ?? "n/a");
Console.WriteLine(result.Explain());
