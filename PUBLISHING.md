# Publishing NetIndex Packages

This document describes how to publish NetIndex packages to NuGet.org and how the release pipeline works.

## Release flow (tag-driven)

1. Ensure all changes are merged to `main` and the full test suite is green.
2. Cut a tag following the `v{MAJOR}.{MINOR}.{PATCH}` convention:
   ```bash
   git tag v0.9.2 && git push origin v0.9.2
   ```
3. `release.yml` triggers automatically and:
   - Builds the solution in Release with `TreatWarningsAsErrors=true`.
   - Runs the full non-Benchmark test suite.
   - Packs all publishable projects (sln-wide) with the tag version, including symbol packages (`.snupkg`).
   - Validates each `.nupkg` for required metadata (title, description, license, repo URL, SourceLink).
   - Pushes `*.nupkg` and `*.snupkg` to `https://api.nuget.org/v3/index.json`.
   - Runs the post-publish smoke test (see [Smoke gate](#smoke-gate) below).
   - Creates a GitHub Release with all artifacts attached.

## Dry-run flow (workflow_dispatch)

Run the pipeline without publishing — useful for testing pack and validation steps:

```bash
gh workflow run release.yml -f publish=false -f version-suffix=rc.1
```

This produces artifacts in the `nupkg` GitHub Actions artifact (downloadable from the run summary) but does **not** push to NuGet.org.

To trigger an intentional pre-release publish:

```bash
gh workflow run release.yml -f publish=true -f version-suffix=rc.1
```

## Required secrets

| Secret | Scope |
|---|---|
| `NUGET_API_KEY` | NuGet.org API key with **Push new packages and package versions** permission for the `NetIndex.*` glob |

Configure under **repo Settings → Secrets and variables → Actions → New repository secret**.

If `NUGET_API_KEY` is absent when a publish is attempted, the workflow fails with a clear annotation rather than a cryptic `dotnet nuget push` error.

## Versioning policy

- Packages are versioned `0.9.x` during the preview phase.
- The version in `Directory.Build.props` acts as the local-dev default (used by `dotnet build` outside CI).
- The **tag** drives the published version: `/p:Version=$VERSION` overrides the props file at pack time.
- Do **not** bump `Directory.Build.props` for each release — the tag is the authoritative version source.
- Versions jump to `1.0.0` after **at least 3 external consumer validations** (per architecture decision). Update `Directory.Build.props` to `1.0.0` at that point and cut a `v1.0.0` tag.

## Smoke gate

After a successful publish the `smoke-test` job in `release.yml` polls NuGet.org (up to 5 minutes) then:

```bash
dotnet new install NetIndex.Template::<VERSION>
dotnet new netindex -n SmokeTest -o /tmp/smoke
cd /tmp/smoke && dotnet restore
```

All three steps must succeed. If the smoke gate fails, the GitHub Release is still created (artifacts are captured) but the workflow run is marked **failed** — visible in `gh run list --workflow release.yml`.

## Package signing

Signing is **not yet implemented** — a code-signing certificate is required.

Tracked as a follow-up story — see `_bmad-output/implementation-artifacts/deferred-work.md`.

This should be addressed before the `1.0.0` release cut.
