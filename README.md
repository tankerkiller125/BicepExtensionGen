# BicepExtensionGen

Generate a **.NET Bicep extension** from any **OpenAPI 3.0** document — including
auto-generated reference documentation.

```
OpenAPI 3.0 (JSON/YAML)  ──►  bicepextgen  ──►  .NET Bicep extension (+ docs/)
```

The generated extension follows the [bicep-local-docgen][docgen] local-deploy template shape
(a REST-calling handler base, an extension `Configuration`, and fully documentation-annotated
model classes), so it builds with `dotnet build`, packages with `bicep publish-extension`, and
its docs are produced by [`bicep-local-docgen`][docgen]. The structure mirrors the
[bicep-extension-template][template] project layout.

## How it works

1. **Parse** the OpenAPI document with `Microsoft.OpenApi`.
2. **Infer resources** by pairing collection/item paths (e.g. `/pets` + `/pets/{petId}`) and
   mapping HTTP verbs onto CRUD:

   | OpenAPI | Bicep handler |
   | --- | --- |
   | `POST` (collection) | Create |
   | `GET` (item) | Read / Preview |
   | `PUT` / `PATCH` (item) | Update |
   | `DELETE` (item) | Delete |

3. **Map schemas** to C#: component schemas → annotated model classes, string enums → C# enums,
   nested objects → nested classes, `readOnly` fields → read-only outputs, path parameters →
   resource identifiers. Nested paths (`/zones/{id}/dns_records/{id}`) carry the parent key as an
   identifier, and colliding resource names are disambiguated by parent path (`AccountUser` vs
   `ZoneUser`), never numeric suffixes.
4. **Map security** from `components.securitySchemes` to `Configuration` credentials. Every
   OpenAPI 3.0 scheme is supported — apiKey (header/query/cookie), HTTP Basic, HTTP Bearer,
   OAuth 2.0, and OpenID Connect. Each becomes an optional, secure config property; the handler
   applies whichever the user supplies (so "email + key" *and* "either/or token" APIs both work).
5. **Emit** the project: `[ResourceType]` / `[TypeProperty]` models with docgen attributes
   (`[BicepDocHeading]`, `[BicepFrontMatter]`, `[BicepDocExample]`), wired REST handlers,
   `Program.cs`, `build.ps1`, and a tool manifest.
6. **Document** (with `--docs`): run `bicep-local-docgen` over the generated models.

## Usage

```bash
dotnet run --project src/BicepExtensionGen -- generate \
  --input ./openapi.yaml \
  --output ./out \
  --name Contoso.PetStore \
  --docs
```

| Option | Description |
| --- | --- |
| `-i, --input` | OpenAPI 3.0 document (JSON or YAML); local path or `http(s)` URL. **Required.** |
| `-o, --output` | Directory to write the generated extension into. **Required.** |
| `-n, --name` | Extension name, e.g. `Contoso.PetStore` (also the C# root namespace). **Required.** |
| `--version` | Extension version (default `0.1.0`). |
| `--namespace` | C# root namespace (default: derived from `--name`). |
| `--mapping` | Resource mapping mode: `path` (default) or `schema`. |
| `--resource-prefix` | Namespace for Bicep resource types, e.g. `Cloudflare.Dns` → `Cloudflare.Dns/DnsRecordA`. |
| `--resource-path` | Hierarchical (Azure-style) child-path types derived from the API path, e.g. `Zone/DnsRecord/A`. Composes with `--resource-prefix`. |
| `--exclude` | Glob(s) to drop matching paths (path mode) or schemas (schema mode). Comma-separated and/or repeatable. |
| `--exclude-tag` | Glob(s) to drop operations by OpenAPI tag. Comma-separated and/or repeatable. |
| `--docs` | Run `bicep-local-docgen` to produce markdown docs under `docs/`. |
| `--force` | Overwrite a non-empty output directory. |

### Excluding parts of the spec

Large public specs often bundle areas you don't care about. `--exclude` and `--exclude-tag`
prune them before any code is generated (and their schemas drop out automatically, since nothing
reachable references them). Globs match the full string, case-insensitively: `*` matches within a
path segment, `**` matches across segments, `?` matches one character.

```bash
# Drop every Workers-AI model endpoint from the Cloudflare spec, by path or by tag:
dotnet run --project src/BicepExtensionGen -- generate \
  --input ./openapi.yaml --output ./out --name Cloudflare.Api \
  --exclude "**/ai/run/**" \
  --exclude-tag "Workers AI*"
```

A path is dropped if its template matches an `--exclude` glob, or if any of its operations carries a
tag matching an `--exclude-tag` glob.

Exit codes: `0` success, `1` error, `2` success with warnings.

## Generated output

```
out/
├── build.ps1                     # build → publish (win/linux/osx) → bicep publish-extension
├── script/publish.ps1            # publish to an OCI registry (e.g. ACR)
├── .config/dotnet-tools.json     # pins bicep-local-docgen
├── src/
│   ├── <name>.csproj
│   ├── Program.cs                # registers every resource handler
│   ├── Models/
│   │   ├── Configuration.cs      # extension baseUrl
│   │   ├── Common.cs             # shared enums + nested types
│   │   └── <Resource>/<Resource>.cs
│   └── Handlers/
│       ├── ResourceHandlerBase.cs
│       └── <Resource>Handler.cs
└── docs/<resource>.md            # when --docs is used
```

## Requirements

- .NET SDK 10.0 or later (the generated extension targets `net10.0`).
- [Bicep CLI][bicep] — only needed to package the generated extension (`build.ps1`).

## Development

```bash
dotnet build BicepExtensionGen.slnx
dotnet test
```

The test suite (`tests/BicepExtensionGen.Tests`) covers resource inference, type mapping, and
file emission against `fixtures/petstore.yaml`.

[docgen]: https://github.com/Gijsreyn/bicep-local-docgen
[template]: https://github.com/maikvandergaag/bicep-extension-template
[bicep]: https://aka.ms/bicep/install
