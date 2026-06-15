# AGENTS.md — Tests

Test-specific guidance. See the [root AGENTS.md](../../AGENTS.md) for repo-wide rules.

## Framework

- [MSTest](https://learn.microsoft.com/dotnet/core/testing/unit-testing-with-mstest)
  (the `MSTest` meta-package). `Microsoft.VisualStudio.TestTools.UnitTesting` is a
  global `Using`, so test files don't import it explicitly.
- Use `[TestClass]` / `[TestMethod]`, `[DataRow]` for parameterized cases, and
  `Assert.*` (no third-party assertion library — keep it that way).

## Running

```bash
dotnet test Tankerkiller125.BicepExtensionGen.Tests.csproj -c Release
# a single test:
dotnet test -c Release --filter "FullyQualifiedName~EmissionTests"
```

## Fixtures

Sample OpenAPI specs live in `fixtures/` (`petstore.yaml`, `auth.yaml`, `dns.yaml`,
`edgecases.yaml`) and are copied next to the test assembly via the `None`/`LinkBase`
item in the csproj. To add a fixture, drop the file in `fixtures/` — the glob picks it
up automatically; no csproj edit is needed. Reference it by relative path from the
test working directory (`fixtures/<name>.yaml`).

## Conventions

- Each source area has a focused test file: resource inference
  (`ResourceModelBuilderTests`), type/auth mapping (`AuthTests`), emission
  (`EmissionTests`), exclusion globs (`ExclusionFilterTests`), resource-type prefixing
  (`ResourceTypePrefixTests`), name disambiguation (`VariantGroupTests`), and odd specs
  (`EdgeCaseTests`). Put new tests with the behavior they cover.
- When you change generator behavior, add or update a test that exercises it against a
  fixture — don't rely on manual verification.
- Tests must be deterministic: no network calls, no wall-clock or randomness, no
  dependence on machine-specific paths.
