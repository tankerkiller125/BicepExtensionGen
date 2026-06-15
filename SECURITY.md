# Security Policy

## Supported versions

BicepExtensionGen is distributed as a [.NET tool][nuget] from a single active
release line. Security fixes are made against the latest released version; please
upgrade to the newest release before reporting an issue.

| Version | Supported |
| ------- | --------- |
| Latest release | ✅ |
| Older releases | ❌ |

## Reporting a vulnerability

**Please do not report security vulnerabilities through public GitHub issues,
discussions, or pull requests.**

Instead, use one of the following private channels:

- **GitHub Security Advisories** (preferred): open a report via the
  [**Security → Report a vulnerability**][advisories] tab on this repository.

Please include as much of the following as you can, so the issue can be triaged
quickly:

- The version of the tool (`bicepextgen --version`) and your .NET SDK version.
- A description of the vulnerability and its impact.
- Steps to reproduce, ideally with a minimal OpenAPI document or command line.
- Any proof-of-concept code or generated output that demonstrates the problem.

You can expect an initial acknowledgement within **14 business days**. I will keep
you updated on progress and coordinate a disclosure timeline with you. Please give
us a reasonable opportunity to release a fix before any public disclosure.

## Scope

This tool reads OpenAPI documents (from local paths or `http(s)` URLs) and
generates C# source for a Bicep extension. Reports are in scope when they concern,
for example:

- Code generation that emits unsafe or injectable C# from a crafted OpenAPI input.
- Path traversal or arbitrary file writes outside the `--output` directory.
- Mishandling of credentials/secrets in generated `Configuration` code.
- Vulnerabilities in the tool's own dependencies that are reachable through it.

Out of scope:

- Vulnerabilities in code you write into a *generated* extension after generation.
- Issues that require an already-compromised build machine or credentials.
- The security of third-party APIs whose OpenAPI specs you generate against.

## Supply chain

Released packages are built reproducibly and published to NuGet.org via
[Trusted Publishing][trusted] (OIDC, no long-lived API keys). Each release also
carries a [build provenance attestation][provenance]; you can verify a downloaded
package with:

```bash
gh attestation verify <file>.nupkg --repo tankerkiller125/BicepExtensionGen
```

[nuget]: https://www.nuget.org/packages/Tankerkiller125.BicepExtensionGen
[advisories]: https://github.com/tankerkiller125/BicepExtensionGen/security/advisories/new
[trusted]: https://learn.microsoft.com/nuget/nuget-org/trusted-publishing
[provenance]: https://docs.github.com/actions/security-guides/using-artifact-attestations-to-establish-provenance-for-builds
