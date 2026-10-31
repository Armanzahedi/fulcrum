# Releasing Fulcrum to NuGet

Three packages ship together with one version: `Fulcrum`, `Fulcrum.Jev`, `Fulcrum.OpenAI`.
The release workflow does the publishing; a maintainer only pushes a tag.

## One-time setup

1. **GitHub repository.** Push this repository to GitHub. The build derives
   `RepositoryUrl` from the GitHub repository name, so nothing in the tree
   hardcodes it. In the repository settings enable *Private vulnerability
   reporting* (SECURITY.md points people there) and *Dependabot* (the config
   is in `.github/dependabot.yml`).
2. **NuGet account.** Sign in at https://www.nuget.org with a Microsoft
   account. Optional but recommended: reserve the `Fulcrum` ID prefix so no
   one else can publish `Fulcrum.*`. There is no button for this; email
   account@nuget.org with your nuget.org profile name and the prefix. Common
   words are often refused, and the team may ask for identity details.
3. **Trusted Publishing policy.** On nuget.org open your profile name →
   *Trusted Publishing → Add*. Repository owner: your GitHub user or org.
   Repository: `fulcrum`. Workflow file: `release.yml` (file name only).
   Environment: leave empty. Scope: *Push new packages and package versions*,
   glob `Fulcrum*`. The workflow then exchanges GitHub's OIDC token for a
   one-hour key at release time; no long-lived key is stored anywhere. A new
   policy may be active for only 7 days until the first successful publish;
   restart the window from the same page if the first release slips.
4. **GitHub secret.** In the repository, *Settings → Secrets and variables →
   Actions → New repository secret*: name `NUGET_USER`, value your nuget.org
   profile name (not the email). Optionally add `OPENROUTER_API_KEY` so CI
   also runs the live tests against Jev on OpenRouter.

## Every release

1. Make sure `main` is green in CI.
2. Update `CHANGELOG.md`: rename `## [Unreleased]` to `## [X.Y.Z] - YYYY-MM-DD`,
   add a fresh empty `## [Unreleased]` above it. The release workflow fails if
   the tagged version has no section.
3. Move every entry in each `PublicAPI.Unshipped.txt` to the matching
   `PublicAPI.Shipped.txt` (leave the `#nullable enable` header in Unshipped).
4. Set `<Version>` in `Directory.Build.props` to `X.Y.Z` so local builds match.
5. Commit, push, then tag and push the tag:

   ```sh
   git tag vX.Y.Z
   git push origin vX.Y.Z
   ```

6. Watch the *release* workflow. It builds with `-p:Version=X.Y.Z`, runs the
   tests, packs the three projects with symbol packages (`.snupkg`), logs in
   to nuget.org through Trusted Publishing, pushes them with
   `--skip-duplicate`, and creates a GitHub release
   whose notes are the CHANGELOG section. Packages appear on nuget.org within a
   few minutes; indexing for `dotnet add package` can take up to an hour.

## Dry run before the first release

```sh
REPO_URL=$(git remote get-url origin | sed -E 's#^git@github.com:#https://github.com/#; s#\.git$##')
dotnet pack -c Release -o artifacts -p:RepositoryUrl=$REPO_URL
unzip -l artifacts/Fulcrum.1.0.0.nupkg          # expect README.md, lib/net10.0/Fulcrum.dll, the .nuspec
```

Then install from the local folder into a scratch project to confirm the
package works as a consumer sees it:

```sh
dotnet new console -o /tmp/fulcrum-smoke && cd /tmp/fulcrum-smoke
dotnet add package Fulcrum --version 1.0.0 --source /path/to/fulcrum/artifacts --source https://api.nuget.org/v3/index.json
```

## Publishing by hand

If the workflow cannot run, the same commands work locally with a personal
API key from nuget.org (*API Keys → Create*, scope push, glob `Fulcrum*`):

```sh
REPO_URL=$(git remote get-url origin | sed -E 's#^git@github.com:#https://github.com/#; s#\.git$##')
dotnet build -c Release -p:Version=X.Y.Z -p:ContinuousIntegrationBuild=true -p:RepositoryUrl=$REPO_URL
dotnet test -c Release --no-build
for p in src/Fulcrum src/Fulcrum.Jev src/Fulcrum.OpenAI; do
  dotnet pack "$p" -c Release --no-build -p:Version=X.Y.Z -p:RepositoryUrl=$REPO_URL -o artifacts
done
dotnet nuget push "artifacts/*.nupkg" --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json --skip-duplicate
```

## Versioning

Semantic versioning. A change to any shipped symbol in `PublicAPI.Shipped.txt`
is a major version. `Fulcrum` and `Fulcrum.OpenAI` follow semver from 1.0.0.
`Fulcrum.Jev` is `[Experimental]`; its API may change in a minor version until
the attribute is removed.
