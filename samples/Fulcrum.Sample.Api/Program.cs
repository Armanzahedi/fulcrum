using Fulcrum;
using Fulcrum.Sample.Api.Identity;
using Fulcrum.Sample.Api.Tickets;
using Fulcrum.Testing;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.AddOpenApi();
builder.Services.AddFulcrum(f =>
{
    f.AddDecisionsFromAssembly(typeof(Program).Assembly);
    if (!string.IsNullOrWhiteSpace(config["OpenRouter:ApiKey"]))
        f.UseOpenAI(o =>
        {
            o.ApiKey = config["OpenRouter:ApiKey"];
            o.BaseAddress = new Uri("https://openrouter.ai/api/v1");
            o.Model = config["OpenRouter:Model"] ?? "typesafe/jev-router";
        });
    else if (!string.IsNullOrWhiteSpace(config["OpenAI:ApiKey"]))
        f.UseOpenAI(o => o.ApiKey = config["OpenAI:ApiKey"]);
    else if (!string.IsNullOrWhiteSpace(config["Jev:ApiKey"]))
        f.UseJev(o => o.ApiKey = config["Jev:ApiKey"]);
    else if (builder.Environment.IsDevelopment())
        f.UseFake(new FakeSystemOne()
            .Answer(Triage.TeamQ, Team.Billing, confidence: 0.90)
            .Answer(Triage.SafeQ, 0.95)
            .Answer(Triage.UrgencyQ, Urgency.CanWait, confidence: 0.80)
            .Answer(IdCheck.FaceQ, new Box(0, 0.62, 0.18, 0.21, 0.30, 0.97))
            .Answer(IdCheck.KindQ, DocumentKind.Passport, confidence: 0.90)
            .Answer(IdCheck.TamperedQ, 0.05));
    else
        throw new InvalidOperationException("No model provider configured: set OpenRouter:ApiKey, OpenAI:ApiKey or Jev:ApiKey");
});

var app = builder.Build();
app.MapOpenApi();
app.MapTickets();
app.MapIdentity();
app.Run();
