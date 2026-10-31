# Contributing

## Build and test

```sh
dotnet build
dotnet test
dotnet publish samples/Fulcrum.Sample.Console -c Release -r <rid>   # AOT check; must report no IL warnings
```

Set `OPENROUTER_API_KEY` to also run `LiveOpenRouterTests` against Jev on OpenRouter (`OPENROUTER_MODEL` overrides `typesafe/jev-router`). Without the key they are skipped. Export the variable in your shell; nothing in the repository loads a `.env` file. Never commit keys.

The build treats warnings as errors. Tests use xunit v3 on Microsoft.Testing.Platform (configured in `global.json`).

## Public API

`Microsoft.CodeAnalysis.PublicApiAnalyzers` runs on every `src/` project. A new public symbol fails the build until it is listed in that project's `PublicAPI.Unshipped.txt`. At release, entries move from `PublicAPI.Unshipped.txt` to `PublicAPI.Shipped.txt`. Removing or changing a shipped symbol is a breaking change.

## Code style

- No code comments, except a one-line note for a constraint the code cannot show.
- No XML docs, except where the behaviour is surprising (`Condition`).
- All I/O is async with `CancellationToken` last; `OperationCanceledException` always propagates.
- No static mutable state.

## Tests

Write the failing test first, watch it fail, then make it pass. Test names describe behaviour in words separated by underscores. Only add tests that pin behaviour someone relies on.
