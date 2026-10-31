# Fulcrum

System One models answer small structured questions with probability distributions.
Your application needs a verb: reply, page someone, send it to a human.
Fulcrum turns typed questions and ordered rules into one committed outcome, and dispatches it to a handler that never sees a probability.

## Install

```sh
dotnet add package Fulcrum            # core, fake model, DI registration
dotnet add package Fulcrum.OpenAI     # Jev on OpenRouter, OpenAI, or any OpenAI-compatible endpoint
dotnet add package Fulcrum.Jev        # native Jev API (experimental)
```

Targets `net10.0`. The full guide is `docs/guide.md` in the repository.

## Quickstart

```csharp
using Fulcrum;
using Fulcrum.Testing;
var fake = new FakeSystemOne().Answer(Spam.IsSpamQ, 0.97);
var result = await new Spam().DecideAsync("WIN A FREE CRUISE!!!", fake);
Console.WriteLine($"{result.Outcome} ({result.Confidence:0.00})");
Console.WriteLine(result.Explain());

public enum SpamOutcome { Block, Deliver }
public sealed class Spam : Decision<string, SpamOutcome>
{
    public static readonly YesNoQuestion IsSpamQ = Question.YesNo("spam", "Is this message unsolicited bulk mail?");

    protected override Content Describe(string message) => message;

    protected override void Define(IRules<SpamOutcome> r)
    {
        r.When(IsSpamQ.Above(0.9)).Then(SpamOutcome.Block);
        r.Default(SpamOutcome.Deliver);
    }
}
```

## A decision with reactions

```csharp
public sealed record Ticket(string Id, string Subject, string Body);
public enum Team { Billing, Technical, Sales }
public enum Urgency { CanWait, ThisWeek, Today }
public enum TriageOutcome { AutoReply, PageOnCall, HumanReview }

public sealed class Triage : Decision<Ticket, TriageOutcome>
{
    public static readonly ChoiceQuestion<Team> TeamQ = Question.Choice<Team>("team",
        "Which team should own this ticket?",
        (Team.Billing,   "Payments, refunds, invoices, subscriptions"),
        (Team.Technical, "Bugs, errors, integrations, outages"),
        (Team.Sales,     "Pricing, plans, new accounts"));

    public static readonly YesNoQuestion SafeQ = Question.YesNo("safe_to_automate",
        "Can a standard reply be sent without a human reading it?");

    public static readonly ScoreQuestion<Urgency> UrgencyQ = Question.Score<Urgency>("urgency",
        "How urgently does this need a human?",
        (Urgency.CanWait,  "Can wait a week"),
        (Urgency.ThisWeek, "Needs attention this week"),
        (Urgency.Today,    "Needs attention today"));

    protected override Content Describe(Ticket t) => $"Subject: {t.Subject}\n\n{t.Body}";

    protected override void Define(IRules<TriageOutcome> r)
    {
        r.WhenAnyConfidenceBelow(0.6).Then(TriageOutcome.HumanReview, name: "low-confidence");
        r.When(UrgencyQ.Is(Urgency.Today)).Then(TriageOutcome.PageOnCall);
        r.When(TeamQ.Is(Team.Billing) && SafeQ.Above(0.85)).Then(TriageOutcome.AutoReply);
        r.Default(TriageOutcome.HumanReview);
        r.OnError(TriageOutcome.HumanReview);
    }

    protected override void React(IReactions<Ticket, TriageOutcome> r)
    {
        r.On(TriageOutcome.AutoReply).With<AutoReplyHandler>();
        r.On(TriageOutcome.PageOnCall).With<PageOnCallHandler>();
        r.On(TriageOutcome.HumanReview).With<QueueForReviewHandler>();
    }
}

public sealed class AutoReplyHandler(IMailer mailer) : IOutcomeHandler<Ticket, TriageOutcome>
{
    public Task HandleAsync(Ticket ticket, DecisionResult<TriageOutcome> result, CancellationToken ct)
        => mailer.SendStandardReplyAsync(ticket, result.Answers.Get(Triage.TeamQ).Value, ct);
}
```

Register once and use it anywhere:

```csharp
services.AddFulcrum(f => f
    .AddDecisionsFromAssembly(typeof(Triage).Assembly)
    .UseOpenAI(o =>
    {
        o.ApiKey = config["OpenRouter:ApiKey"];
        o.BaseAddress = new Uri("https://openrouter.ai/api/v1");
        o.Model = "typesafe/jev-router";
    }));

var ran = await triage.RunAsync(ticket, ct);        // decide, then dispatch to the handler
var decided = await triage.DecideAsync(ticket, ct); // decide only, no side effects
```

Only questions referenced by a rule are sent to the model. Rules are first-match; `Default` is required; `OnError` turns a provider failure into a fallback outcome. A mistake in a decision (no default, an outcome without a handler, a threshold outside `[0, 1]`) throws `FulcrumException` from `AddFulcrum`, so the host never starts with it.

`&&` and `||` on conditions build a rule tree. They do not short-circuit.

## Why not just call the API

- Every outcome has a reaction (when `React` is declared), checked when the host starts.
- The reaction receives the evidence (typed answers, explanation) without re-plumbing it.
- Rules are testable with a scripted fake model. Handlers are plain classes you call with the result of a fake `DecideAsync`.
- Cross-cutting concerns wrap decide-then-react in one place.

## Providers

| Call | Package | Talks to |
|---|---|---|
| `UseOpenAI(o => ...)` | `Fulcrum.OpenAI` | any OpenAI-compatible `chat/completions` endpoint with strict JSON schema: Jev on OpenRouter, OpenAI and others. The key comes from `OpenAIOptions.ApiKey`, or from `OPENAI_API_KEY` for the default `api.openai.com` only |
| `UseJev(o => ...)` | `Fulcrum.Jev` | Jev's native `POST /v1/systemone`. The key comes from `JevOptions.ApiKey` or `JEV_API_KEY`. Experimental |
| `UseFake(fake)` | `Fulcrum` | a scripted `FakeSystemOne`, for tests and demos |
| `UseClient<T>()` / `UseClient(sp => ...)` | `Fulcrum` | your own `ISystemOne` |

Jev on OpenRouter, verified live:

```csharp
services.AddFulcrum(f => f
    .AddDecision<Triage>()
    .UseOpenAI(o =>
    {
        o.ApiKey = config["OpenRouter:ApiKey"];
        o.BaseAddress = new Uri("https://openrouter.ai/api/v1");
        o.Model = "typesafe/jev-router";
    }));
```

- Configure exactly one provider.
- Both network providers retry 429, 502, 503, 504 and connection failures: 3 attempts, `Retry-After` honoured, within a 30 s budget.
- The OpenAI-compatible provider re-asks the model with its list of problems when an answer fails validation.
- Provider failures surface as `SystemOneException`, which `OnError` turns into a fallback outcome.

`Fulcrum.Jev` is `[Experimental("FUL0001")]` until its native wire format is verified against the live endpoint. Opt in with `<NoWarn>$(NoWarn);FUL0001</NoWarn>`.

To write your own provider, implement `ISystemOne.AskAsync(Content, IReadOnlyList<Question>, CancellationToken)` and return raw answers keyed by question name. `Answers.Validate(content, questions)` is public if you want to re-ask on bad output.

## Explain()

Every result explains itself:

```
Triage  outcome AutoReply  rule AutoReply  confidence 0.90

answers
  urgency            CanWait  0.80   {CanWait 0.80, ThisWeek 0.10, Today 0.10}
  team               Billing  0.90   {Billing 0.90, Technical 0.05, Sales 0.05}
  safe_to_automate   p(yes) 0.95

rules
  ✗ low-confidence   any confidence < 0.6                    min 0.80
  ✗ PageOnCall       urgency=Today                           observed CanWait
  ✓ AutoReply        team=Billing && safe_to_automate>0.85
                     team=Billing            Billing   ✓
                     safe_to_automate>0.85   0.95      ✓
  · default          HumanReview
```

## Samples

- `samples/Fulcrum.Sample.Console` is the Triage decision. It uses the fake by default, live Jev on OpenRouter when `OPENROUTER_API_KEY` is set, or the native Jev API when `JEV_API_KEY` is set:

  ```sh
  dotnet run --project samples/Fulcrum.Sample.Console
  OPENROUTER_API_KEY=... dotnet run --project samples/Fulcrum.Sample.Console -- "Production is down" "Every API call returns 500"
  ```

- `samples/Fulcrum.Sample.Transactions` is a card-authorization decision (`FraudScreen`): approve, step up to 3-D Secure, decline, or send to an analyst. Run it with a scenario name:

  ```sh
  dotnet run --project samples/Fulcrum.Sample.Transactions -- groceries   # also: crypto, fraud
  ```

- `samples/Fulcrum.Sample.Api` is a minimal API with `POST /tickets` (Triage) and `POST /applicants/{id}/id-document` (IdCheck, a multipart photo). It discovers decisions by scanning and picks the provider from configuration in this order: `OpenRouter:ApiKey`, `OpenAI:ApiKey`, `Jev:ApiKey`. With no key it uses the fake in the Development environment and refuses to start anywhere else.

## Tests

```sh
dotnet test                                   # unit tests; the live tests are skipped
OPENROUTER_API_KEY=... dotnet test            # also runs the live tests against Jev on OpenRouter
```

## AOT

Every package is AOT- and trim-compatible. `AddDecisionsFromAssembly` uses reflection; in AOT applications register each decision with `AddDecision<T>()`. `samples/Fulcrum.Sample.Console` publishes with `PublishAot=true`.

## License

MIT. No license keys, no telemetry, no phone-home.
