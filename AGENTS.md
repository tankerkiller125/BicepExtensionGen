# AGENTS.md

Guidance for AI coding agents working in this repository. Human contributors should
read [CONTRIBUTING.md](CONTRIBUTING.md); the conventions here apply to both.

This project is primarily AI-generated (see the AI disclosure in [README.md](README.md)).
That is not licence to lower the bar: every change must build warning-free, keep the
test suite green, and be reviewable by a human.

## What this project is

`bicepextgen` is a .NET 10 command-line tool (packaged as a [dotnet tool][tool]) that
reads an OpenAPI 3.0 document and generates a .NET Bicep extension — annotated model
classes, REST handlers, `Program.cs`, and build scripts. See [README.md](README.md)
for the end-to-end design.

## Layout

- `src/Tankerkiller125.BicepExtensionGen/` — the tool itself.
  - `Cli/` — argument parsing and the `generate` command.
  - `OpenApi/` — loading, filtering, and mapping OpenAPI into the internal model.
  - `Model/` — the intermediate `ExtensionModel`/`ResourceModel`/`PropertyModel` types.
  - `Generation/` — code emitters that turn the model into C# source and project files.
- `tests/Tankerkiller125.BicepExtensionGen.Tests/` — MSTest suite. See its own
  [AGENTS.md](tests/Tankerkiller125.BicepExtensionGen.Tests/AGENTS.md).
- `.github/workflows/` — `ci.yml` (build/test) and `release.yml` (pack/attest/publish).

## Build, test, run

The solution file is `Tankerkiller125.BicepExtensionGen.slnx`.

```bash
dotnet restore Tankerkiller125.BicepExtensionGen.slnx --locked-mode   # mirrors CI
dotnet build  Tankerkiller125.BicepExtensionGen.slnx -c Release
dotnet test   Tankerkiller125.BicepExtensionGen.slnx -c Release

# run from source
dotnet run --project src/Tankerkiller125.BicepExtensionGen -- generate \
  --input ./openapi.yaml --output ./out --name Contoso.PetStore
```

Always run build and test before declaring a change done. Do not report a task as
complete if tests fail — surface the failure instead.

## Conventions

- Target framework is `net10.0`; the SDK is pinned in `global.json`.
- Nullable reference types and implicit usings are enabled — honour both.
- Match the surrounding style. Comments explain *why*, not *what*, and stay at the
  density of nearby code. Prefer reusing existing helpers (e.g. `NameUtil`) over new
  ad-hoc string munging.
- Reproducible-build, Source Link, and lock-file settings live in
  `Directory.Build.props`. Don't duplicate them per-project.

## Dependencies and lock files

This repo commits `packages.lock.json` and CI restores with `--locked-mode`. If you
change any `PackageReference`, regenerate and commit the lock files:

```bash
dotnet restore Tankerkiller125.BicepExtensionGen.slnx
```

A PR whose package references and lock files disagree will fail CI.

## Release & supply chain — do not touch casually

- Releases trigger on a `v*` SemVer tag; the version is derived from the tag, so do
  **not** hand-bump `<Version>` in the csproj for a release.
- Publishing uses NuGet Trusted Publishing (OIDC) plus build-provenance attestation,
  scoped to the protected `release` environment. Changing `release.yml`, the pinned
  action SHAs, or the workflow filename can break trusted publishing — flag such
  changes for human review rather than applying them silently.

## Safety

- Never commit secrets. There are intentionally no long-lived publish tokens in this
  repo (trusted publishing replaced them).
- Security issues follow [SECURITY.md](SECURITY.md) — never open a public issue/PR for
  a vulnerability.
- Licensed under MPL-2.0; keep new files compatible.

[tool]: https://learn.microsoft.com/dotnet/core/tools/global-tools
