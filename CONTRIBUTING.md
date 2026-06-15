# Contributing to BicepExtensionGen

Thanks for your interest in improving BicepExtensionGen! This document covers how
to set up your environment, make changes, and submit them.

By contributing, you agree that your contributions will be licensed under the
project's [Mozilla Public License 2.0](LICENSE).

## Prerequisites

- [.NET SDK 10.0 or later](https://dotnet.microsoft.com/download) — the version is
  pinned in [`global.json`](global.json).
- A [Bicep CLI][bicep] install is only needed if you want to package a *generated*
  extension; it isn't required to build or test this repo.

## Building and testing

The solution file is `Tankerkiller125.BicepExtensionGen.slnx`.

```bash
# Restore (locked mode mirrors CI — fails if a lock file is stale)
dotnet restore Tankerkiller125.BicepExtensionGen.slnx --locked-mode

# Build
dotnet build Tankerkiller125.BicepExtensionGen.slnx -c Release

# Test
dotnet test Tankerkiller125.BicepExtensionGen.slnx -c Release
```

To run the tool from source while developing:

```bash
dotnet run --project src/Tankerkiller125.BicepExtensionGen -- generate \
  --input ./openapi.yaml --output ./out --name Contoso.PetStore
```

The test suite (`tests/Tankerkiller125.BicepExtensionGen.Tests`) covers resource
inference, type mapping, and file emission. Please add or update tests for any
behavior change.

## Dependencies and lock files

This repo uses committed `packages.lock.json` files and restores in `--locked-mode`
on CI. If you add, remove, or upgrade a `PackageReference`, regenerate the lock
files and commit the result:

```bash
dotnet restore Tankerkiller125.BicepExtensionGen.slnx
```

A pull request whose package references and lock files disagree will fail CI.

## Coding guidelines

- Match the style of the surrounding code; the repo uses nullable reference types
  and implicit usings (`Nullable`/`ImplicitUsings` are enabled).
- Keep comments at the same density and altitude as nearby code — explain *why*,
  not *what*.
- Run `dotnet build` warning-free and `dotnet test` green before opening a PR.

## Submitting changes

1. Fork the repo and create a topic branch off `master`.
2. Make your change with accompanying tests.
3. Ensure `dotnet build` and `dotnet test` pass, and lock files are up to date.
4. Open a pull request describing **what** changed and **why**. Link any related
   issue. The CI workflow must pass before review.

Keep pull requests focused — smaller, single-purpose changes are easier to review
and land faster.

## Reporting bugs and requesting features

- For **security vulnerabilities**, follow [SECURITY.md](SECURITY.md) — do not open
  a public issue.
- For bugs and feature requests, open a GitHub issue. A minimal OpenAPI document
  and the exact command line that reproduces the problem are extremely helpful.

## Releases

Releases are cut by maintainers by pushing a SemVer tag (e.g. `v0.1.0`), which
triggers the release workflow: it builds reproducibly, attests build provenance,
publishes to NuGet.org via Trusted Publishing, and creates a GitHub Release. The
package version is derived from the tag, so no version bump is needed in the
project file.

[bicep]: https://aka.ms/bicep/install
