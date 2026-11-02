using System.Globalization;
using System.Text;
using Fulcrum;
using Fulcrum.Sample.Transactions.Authorizations;
using Fulcrum.Testing;
using Microsoft.Extensions.DependencyInjection;

Console.OutputEncoding = Encoding.UTF8;

var name = args.Length > 0 ? args[0] : "groceries";
if (!Scenarios.All.TryGetValue(name, out var scenario))
{
    Console.Error.WriteLine($"Unknown scenario '{name}'. Choose one of: {string.Join(", ", Scenarios.All.Keys)}");
    return 2;
}

var openRouterKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
var jevKey = Environment.GetEnvironmentVariable("JEV_API_KEY");

using var services = new ServiceCollection()
    .AddFulcrum(f =>
    {
        f.AddDecision<FraudScreen>();
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
            var fake = new FakeSystemOne();
            scenario.Script(fake);
            f.UseFake(fake);
        }
    })
    .BuildServiceProvider();

var result = await services.GetRequiredService<FraudScreen>().RunAsync(scenario.Authorization);

Console.WriteLine();
Console.WriteLine(result.Outcome);
Console.WriteLine(result.Confidence?.ToString(CultureInfo.InvariantCulture) ?? "n/a");
Console.WriteLine(result.Explain());
return 0;
