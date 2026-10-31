# Fulcrum guide

This guide covers everything a decision author needs: questions, rules, outcomes, reactions, registration, providers, testing, errors and limits.

- [Concepts](#concepts)
- [Questions](#questions)
- [Content and images](#content-and-images)
- [Rules and conditions](#rules-and-conditions)
- [Confidence](#confidence)
- [Reactions](#reactions)
- [Registration](#registration)
- [Providers](#providers)
- [Testing](#testing)
- [Errors](#errors)
- [Explain()](#explain)
- [AOT and trimming](#aot-and-trimming)
- [Limits](#limits)

## Concepts

A **decision** is a class deriving from `Decision<TState, TOutcome>`. It has:

- `Describe(state)`: turns your state into `Content`, the text and images the model sees. It must be pure.
- `Define(rules)`: ordered rules over **questions**, a required `Default`, and an optional `OnError`.
- `React(reactions)`, optional: one handler per outcome.

Fulcrum sends only the questions your rules reference to the model, validates the answers, evaluates the rules in order (first match wins), and returns a `DecisionResult<TOutcome>`. `RunAsync` also dispatches the outcome's handler. Handlers see typed answers and the explanation, never a bare threshold.

## Questions

Declare questions as `public static readonly` fields so handlers and tests can use them.

| Kind | Factory | Answer | Confidence |
|---|---|---|---|
| Choice | `Question.Choice<TEnum>(name, text, (value, description)...)` | one enum member plus a distribution over all options | probability of the chosen option |
| Yes/no | `Question.YesNo(name, text)` | `p`, the probability of yes | `max(p, 1 - p)` |
| Score | `Question.Score<TEnum>(name, text, (level, description)...)` | one level plus a distribution over the levels | probability of the chosen level |
| Box | `Question.Box(name, text)` | zero or more boxes `(image, x, y, width, height, confidence)`, normalised to `[0, 1]`, origin top-left | none |

Rules for declarations:

- Names are required and unique within a decision. They key the answers, appear on the wire and appear in `Explain()`.
- A choice needs at least two distinct options.
- A score must list every member of its enum exactly once. **The listed order is the scale**: `AtLeast` and `AtMost` compare positions in that list, not the enum's numeric values.

Read answers with `result.Answers.Get(question)`: `Value`, `Confidence` and `Probabilities` for choices and scores, `Probability` for yes/no, and `Boxes` for boxes.

## Content and images

```csharp
protected override Content Describe(Ticket t) => $"Subject: {t.Subject}\n\n{t.Body}";   // string converts implicitly

protected override Content Describe(IdDocument d) =>
    Content.From($"Declared type: {d.DeclaredType}").WithImage(d.Photo, d.MediaType);   // or WithImage(uri)
```

`Describe` must be declared as returning `Content`. A string converts to it implicitly. Image-only content comes from `Content.Image(bytes, mediaType)` or `Content.Image(uri)`. A decision that asks a box question with content that has no image throws `FulcrumException` before any model call.

## Rules and conditions

```csharp
protected override void Define(IRules<TriageOutcome> r)
{
    r.WhenAnyConfidenceBelow(0.6).Then(TriageOutcome.HumanReview, name: "low-confidence");
    r.When(UrgencyQ.Is(Urgency.Today)).Then(TriageOutcome.PageOnCall);
    r.When(TeamQ.Is(Team.Billing) && SafeQ.Above(0.85)).Then(TriageOutcome.AutoReply);
    r.Default(TriageOutcome.HumanReview);
    r.OnError(TriageOutcome.HumanReview);
}
```

| Question | Conditions |
|---|---|
| Choice | `Is(v)`, `IsNot(v)`, `ConfidenceAbove(t)` |
| Yes/no | `Above(t)`, `Below(t)` on `p` |
| Score | `Is(l)`, `IsNot(l)`, `AtLeast(l)`, `AtMost(l)`, `ConfidenceAbove(t)` |
| Box | `Found()`, `None()`, `FoundAbove(t)` (any box's confidence), `Count().Is/AtLeast/Above/Below(n)` |

- Combine conditions with `&&`, `||` and `!`. They build a tree and **do not short-circuit**; every leaf is evaluated and shown in `Explain()`.
- Threshold comparisons are strict (`Above(0.9)` is false at exactly 0.9). `AtLeast` and `AtMost` are inclusive.
- `WhenAnyConfidenceBelow(t)` matches when any asked non-box question's confidence is below `t`. Box questions never count: finding nothing is a confident answer.
- A rule's name defaults to its outcome's name. Two rules with the same outcome must name at least one of them. `default` is reserved.

## Confidence

`result.Confidence` is the minimum confidence over the non-box questions the matched rule referenced. For `WhenAnyConfidenceBelow` and the default it is the minimum over all asked non-box questions. It is `null` when no non-box question contributed, and on a fallback. `IsFallback` tells those two apart.

Confidence is always derived from the distribution. Providers drop any separate confidence field on the wire, so a condition and `Explain()` can never disagree.

## Reactions

```csharp
protected override void React(IReactions<Ticket, TriageOutcome> r)
{
    r.On(TriageOutcome.AutoReply).With<AutoReplyHandler>();       // resolved from DI, transient
    r.On(TriageOutcome.PageOnCall).With((t, result, ct) => ...);  // a delegate with no dependencies
    r.On(TriageOutcome.HumanReview).Nothing();                    // an explicit no-op
}
```

- `React` is all-or-nothing: once `React` maps any outcome, every outcome must be mapped, so a forgotten outcome is a startup error. A decision that maps none can only use `DecideAsync`.
- Handlers are registered as transient services. `RunAsync` opens a service scope for the call, resolves the handler from it and disposes it afterwards, so scoped dependencies such as a `DbContext` work and disposable handlers are released. The scope is the handler's own, not the caller's: a `DbContext` in the handler is a different instance from the one in the surrounding web request.
- Handler exceptions propagate out of `RunAsync`. The decision was made, and swallowing the exception would hide a failed action.
- `RunAsync` on a decision without reactions throws `FulcrumException` before calling the model. Use `DecideAsync` for those.

## Registration

```csharp
services.AddFulcrum(f => f
    .AddDecision<Triage>()                                   // AOT-safe
    // or: .AddDecisionsFromAssembly(typeof(Triage).Assembly)  // reflection; registers every decision; not for AOT
    .UseOpenAI(o => { ... }));                               // exactly one provider
```

`AddFulcrum` builds and validates every decision's rules and reactions, and throws `FulcrumException` on any mistake, so the host never starts with a broken decision. It catches:

- no rules or no `Default`
- duplicate or reserved rule names
- a rule set that references no question
- duplicate question names
- invalid choice or score declarations
- a condition on an undeclared option
- thresholds outside `[0, 1]` and negative counts
- outcome values outside the enum, in `Then`, `Default`, `OnError` or `React`
- partial reactions
- no provider, or more than one provider

Call `AddFulcrum` once per service collection; a second call throws. Each decision is a singleton per service provider. Scanning skips abstract and generic types and types without a public parameterless constructor.

## Providers

### Jev on OpenRouter (verified live)

```csharp
f.UseOpenAI(o =>
{
    o.ApiKey = config["OpenRouter:ApiKey"];
    o.BaseAddress = new Uri("https://openrouter.ai/api/v1");
    o.Model = "typesafe/jev-router";
});
```

### Any OpenAI-compatible endpoint

`UseOpenAI` posts to `{BaseAddress}/chat/completions` with `temperature: 0` and a strict JSON schema built from your questions. If an answer fails validation (an undeclared option, a missing field, a probability outside `[0, 1]`), the provider shows the model its problems and asks again, up to `MaxAttempts` times (default 3). The key falls back to `OPENAI_API_KEY` only when `BaseAddress` is the default `api.openai.com`, so an OpenAI key is never sent to another host. `BaseAddress` must be absolute, without a query; a trailing `/` is optional. Images are sent as `image_url` parts, so use a vision-capable model when your content has images.

### Native Jev API (experimental)

`UseJev(o => o.ApiKey = ...)` speaks Jev's native `POST /v1/systemone` format. The format was reconstructed from a brief and has not been verified against the live endpoint, so the package is marked `[Experimental("FUL0001")]`. Suppress the diagnostic with `<NoWarn>$(NoWarn);FUL0001</NoWarn>` to opt in. The key falls back to `JEV_API_KEY` with the default `BaseAddress`.

### Both network providers

- Retry 429, 502, 503, 504 and connection failures: 3 attempts, `Retry-After` honoured, otherwise exponential backoff from 200 ms with jitter, within a 30 s budget. When `Retry-After` would exceed the budget, the response is returned at once and surfaces as `SystemOneException` with the status.
- Map non-2xx responses, network failures, timeouts and unreadable bodies to `SystemOneException`. The message includes up to the first 500 characters of the body.
- Use `IHttpClientFactory` named clients (`Fulcrum.Jev`, `Fulcrum.OpenAI`). Register a `TimeProvider` to control the retry clock.

### Your own provider

Implement `ISystemOne`:

```csharp
public sealed class MyModel : ISystemOne
{
    public Task<Answers> AskAsync(Content state, IReadOnlyList<Question> questions, CancellationToken ct) { ... }
}
// f.UseClient<MyModel>() or f.UseClient(sp => new MyModel(...))
```

Return one raw answer per question, keyed by name: `ChoiceAnswer`, `YesNoAnswer`, `ScoreAnswer` or `BoxAnswer`. Fulcrum validates them. Call `answers.Validate(state, questions)` yourself if you want to re-ask on bad output.

## Testing

Test rules with the scripted fake. No network, no DI:

```csharp
var fake = new FakeSystemOne()
    .Answer(Triage.TeamQ, Team.Billing, confidence: 0.90)   // remaining mass spread evenly
    .Answer(Triage.SafeQ, 0.95)
    .Answer(Triage.UrgencyQ, Urgency.CanWait, confidence: 0.80);

var result = await new Triage().DecideAsync(ticket, fake);

Assert.Equal(TriageOutcome.AutoReply, result.Outcome);
Assert.Equal(["urgency", "team", "safe_to_automate"], fake.Requests.Single().Questions.Select(q => q.Name));
```

- `Fail(exception)` makes the next call throw, which exercises `OnError`.
- Asking a question the fake has no answer for throws `InvalidOperationException` naming the question, even when `OnError` is declared, so a test never passes by accident.
- Test handlers as plain classes, passing the result of `DecideAsync(state, fake)`. For `RunAsync` end to end, register with `UseFake(fake)`.

## Errors

| Exception | Meaning | `OnError` absorbs it? |
|---|---|---|
| `FulcrumException` | a programmer error: a bad declaration, a missing provider, empty content from `Describe`, a box question without an image, an unbound decision | no |
| `SystemOneException` | provider failure, timeout, or invalid answers (`Violations` lists them) | yes |
| `OperationCanceledException` | your token was cancelled | no |
| any other exception from a provider | a bug in that provider | no |
| anything thrown by `Describe` or a handler | your bug | no |

## Explain()

```
Triage  outcome PageOnCall  rule PageOnCall  confidence 0.99

answers
  urgency            Today      0.99   {CanWait 0.00, ThisWeek 0.01, Today 0.99}
  team               Technical  0.98   {Billing 0.01, Technical 0.98, Sales 0.01}
  safe_to_automate   p(yes) 0.01

rules
  ✗ low-confidence   any confidence < 0.6                    min 0.98
  ✓ PageOnCall       urgency=Today
  · AutoReply        team=Billing && safe_to_automate>0.85
  · default          HumanReview
```

The block above is a real answer from Jev for "Production is down". The markers mean: `✓` matched, `✗` evaluated and not matched, `·` not evaluated. A fallback shows the error instead of answers and rules.

## AOT and trimming

All three packages are `IsAotCompatible`. The only reflection path is `AddDecisionsFromAssembly`, which is annotated `[RequiresUnreferencedCode]`. AOT applications register decisions with `AddDecision<T>()`. The console sample is published with `PublishAot=true` in CI.

## Limits

- Choices, scores and outcomes must be enums.
- Images travel in memory and as base64 on the wire. Fulcrum enforces no size limit; the provider's limit surfaces as `SystemOneException`.
- The 30 s retry budget covers sending and receiving response headers. Reading the body is bounded by `HttpClient.Timeout` (100 s by default).
- Box answers from general-purpose LLMs are approximate. Treat them as hints.
- No audit trail, caching or telemetry in v1. `ISystemOne` and `DecisionResult` are the seams to add them.
