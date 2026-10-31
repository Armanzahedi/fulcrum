# Changelog

All notable changes to this project are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Changed

- `Fulcrum`: `RunAsync` resolves the outcome handler inside a per-call service scope. Scoped dependencies now work in handlers, and disposable handlers or dependencies no longer stay alive in the root provider until shutdown.
- `Fulcrum`: choice and score questions compute their option names once instead of on every rule evaluation, validation and prompt build.
- `Fulcrum.OpenAI`, `Fulcrum.Jev`: dropped the custom primary handler; `IHttpClientFactory` already rotates handlers every two minutes.

## [1.0.0] - 2026-10-31

### Added

- `Fulcrum`: typed questions (`Choice<TEnum>`, `YesNo`, `Score<TEnum>`, `Box`) over text and image `Content`.
- `Fulcrum`: conditions built with `&&`, `||` and `!` on question handles; first-match rules with a required `Default`, an optional `OnError` and `WhenAnyConfidenceBelow`.
- `Fulcrum`: `Decision<TState, TOutcome>` with `DecideAsync` and `RunAsync`; outcome handlers through `React` with `With<THandler>()`, `With(delegate)` and `Nothing()`.
- `Fulcrum`: startup validation of rules, questions, thresholds and reactions from `AddFulcrum`.
- `Fulcrum`: `AddFulcrum` with `AddDecision<T>()`, `AddDecisionsFromAssembly`, `UseClient` and `UseFake`.
- `Fulcrum`: `Answers.Validate`, `DecisionResult.Explain()` and `FakeSystemOne` in `Fulcrum.Testing`.
- `Fulcrum.Jev` (experimental, `FUL0001`): `UseJev` and `JevClient`.
- `Fulcrum.OpenAI`: `UseOpenAI` and `OpenAIClient` for any OpenAI-compatible endpoint, with strict JSON schema output and a validation re-ask loop. Verified live against Jev on OpenRouter (`typesafe/jev-router`).
- Retries for 429, 502, 503, 504 and connection failures in both providers.
- Startup validation also rejects rule sets that reference no question, outcome values outside the enum, and a second provider.
- Provider keys fall back to `OPENAI_API_KEY` / `JEV_API_KEY` only with the default `BaseAddress`, so a key never reaches another host. `BaseAddress` must be absolute without a query; a trailing `/` is optional.
- Opt-in live tests (`OPENROUTER_API_KEY`) and samples that run against Jev on OpenRouter.

### Notes

- `Fulcrum.Jev` remains `[Experimental("FUL0001")]`: Jev's native wire format is not yet verified against the live endpoint.
