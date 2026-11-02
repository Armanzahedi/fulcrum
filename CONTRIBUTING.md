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

## Releasing

Three packages ship together with one version: `Fulcrum`, `Fulcrum.Jev`, `Fulcrum.OpenAI`. Pushing a `vX.Y.Z` tag runs the release workflow, which builds, tests, packs and publishes to nuget.org through Trusted Publishing and creates a GitHub release from the matching `CHANGELOG.md` section. Before tagging: rename `## [Unreleased]` to `## [X.Y.Z] - YYYY-MM-DD` and add a fresh empty `## [Unreleased]`, move every entry in each `PublicAPI.Unshipped.txt` to `PublicAPI.Shipped.txt`, and set `<Version>` in `Directory.Build.props`. A change to a shipped symbol is a major version; `Fulcrum.Jev` is `[Experimental]` and may change in a minor version until the attribute is removed.
