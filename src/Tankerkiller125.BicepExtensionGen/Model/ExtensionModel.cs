namespace Tankerkiller125.BicepExtensionGen.Model;

/// <summary>
/// Top-level intermediate representation of a Bicep extension to be generated
/// from an OpenAPI document. Produced by <see cref="OpenApi.ResourceModelBuilder"/>
/// and consumed by the generators under <c>Generation/</c>.
/// </summary>
public sealed class ExtensionModel
{
    /// <summary>Extension name, e.g. "Contoso.Api". Also used as the C# root namespace.</summary>
    public required string Name { get; init; }

    /// <summary>Assembly / output binary name, e.g. "contoso-api".</summary>
    public required string AssemblyName { get; init; }

    /// <summary>Semantic version of the extension, e.g. "0.1.0".</summary>
    public required string Version { get; init; }

    /// <summary>Default API base URL taken from <c>servers[0].url</c> (may be null).</summary>
    public string? BaseUrl { get; init; }

    /// <summary>
    /// Optional namespace prefix for Bicep resource types, e.g. <c>Cloudflare.Dns</c>, producing
    /// Azure-style type names like <c>Cloudflare.Dns/dnsRecordA</c>. Null/empty leaves type names bare.
    /// </summary>
    public string? ResourceTypePrefix { get; init; }

    /// <summary>
    /// When true, resource types use their hierarchical <see cref="ResourceModel.TypePath"/>
    /// (e.g. <c>zones/dnsRecords/A</c>) instead of the flat name — Azure-style parent/child typing.
    /// </summary>
    public bool UseResourceTypePath { get; init; }

    /// <summary>
    /// The Bicep resource-type string for a resource: the flat name or, when child paths are enabled,
    /// the <c>/</c>-joined hierarchy — each optionally prefixed with <see cref="ResourceTypePrefix"/>.
    /// </summary>
    public string QualifiedResourceType(ResourceModel resource)
    {
        var core = UseResourceTypePath && resource.TypePath.Count > 0
            ? string.Join("/", resource.TypePath)
            : resource.ResourceTypeName;
        return string.IsNullOrEmpty(ResourceTypePrefix) ? core : $"{ResourceTypePrefix}/{core}";
    }

    /// <summary>Authentication schemes derived from <c>components.securitySchemes</c>.</summary>
    public List<SecuritySchemeModel> SecuritySchemes { get; } = [];

    /// <summary>Resources inferred from the path/operation structure.</summary>
    public List<ResourceModel> Resources { get; } = [];

    /// <summary>Enums shared across resources (deduplicated by name).</summary>
    public List<EnumModel> Enums { get; } = [];

    /// <summary>Nested object types shared across resources (deduplicated by name).</summary>
    public List<NestedTypeModel> NestedTypes { get; } = [];

    /// <summary>Non-fatal issues encountered while building the model.</summary>
    public List<string> Warnings { get; } = [];
}

/// <summary>
/// A single OpenAPI security scheme mapped to credential inputs on the extension
/// <c>Configuration</c> and the way they are applied to outgoing HTTP requests.
/// Every defined scheme contributes optional credentials; whichever the user supplies
/// are applied, which handles both "all required" (e.g. email + key) and "either/or" APIs.
/// </summary>
public sealed class SecuritySchemeModel
{
    /// <summary>The key under <c>components.securitySchemes</c>, e.g. "api_token".</summary>
    public required string Key { get; init; }

    public required SecurityKind Kind { get; init; }

    /// <summary>For apiKey schemes: the header/query/cookie parameter name.</summary>
    public string? ParameterName { get; init; }

    /// <summary>Primary credential <c>Configuration</c> property (the key/token, or username for Basic).</summary>
    public required string PrimaryProperty { get; init; }

    /// <summary>Secondary credential property (the password for Basic auth).</summary>
    public string? SecondaryProperty { get; init; }

    /// <summary>Human-readable description of how the credential is used.</summary>
    public required string Description { get; init; }
}

public enum SecurityKind
{
    ApiKeyHeader,
    ApiKeyQuery,
    ApiKeyCookie,
    HttpBasic,
    HttpBearer,
    OAuth2,
    OpenIdConnect,
    Unsupported,
}
