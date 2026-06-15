namespace Tankerkiller125.BicepExtensionGen.Model;

/// <summary>
/// A single Bicep resource type inferred from a collection/item pair of OpenAPI paths
/// (e.g. <c>/pets</c> + <c>/pets/{petId}</c>).
/// </summary>
public sealed class ResourceModel
{
    /// <summary>PascalCase singular C# type name, e.g. "Pet".</summary>
    public required string Name { get; init; }

    /// <summary>The Bicep resource type name surfaced to users (defaults to <see cref="Name"/>).</summary>
    public required string ResourceTypeName { get; init; }

    /// <summary>
    /// The singularized, PascalCased non-parameter path segments (root→leaf) this resource was
    /// derived from, e.g. <c>["Account", "User"]</c> for <c>/accounts/{id}/users</c>. Used to
    /// disambiguate name collisions with parent context instead of numeric suffixes.
    /// </summary>
    public IReadOnlyList<string> NameSegments { get; init; } = [];

    /// <summary>Front-matter category (usually the OpenAPI tag).</summary>
    public string? Category { get; init; }

    /// <summary>
    /// Hierarchical segments for an Azure-style child-path resource type, e.g. <c>[Zone, DnsRecord, A]</c>
    /// renders as <c>zones/dnsRecords/A</c>. Used only when child-path type naming is enabled; the flat
    /// <see cref="Name"/> always drives the C# class. Deduplicated across resources.
    /// </summary>
    public IReadOnlyList<string> TypePath { get; set; } = [];

    /// <summary>
    /// When set, the resource belongs to a variant group: its model file lives under
    /// <c>Models/&lt;Group&gt;/</c> and its class shares the <c>Models.&lt;Group&gt;</c> namespace with the
    /// group's abstract parent base and sibling variants. Null for standalone resources, which keep
    /// the flat <c>Models/&lt;Name&gt;.cs</c> layout and their own <c>Models.&lt;Name&gt;</c> namespace.
    /// </summary>
    public string? Group { get; init; }

    /// <summary>
    /// Base class the resource class derives from. Defaults to <c>&lt;Name&gt;Identifiers</c> (the
    /// standalone pattern). A variant resource derives from its group's parent base instead.
    /// </summary>
    public string? BaseTypeName { get; init; }

    /// <summary>
    /// Identifiers class used by the typed handler. Defaults to <c>&lt;Name&gt;Identifiers</c>. Variants
    /// in a group share the parent base's identifiers class.
    /// </summary>
    public string? IdentifiersTypeName { get; init; }

    /// <summary>Whether this resource owns (emits) its identifiers class. False for variants, which inherit it.</summary>
    public bool OwnsIdentifiers { get; init; } = true;

    /// <summary>
    /// Whether to surface this as a deployable Bicep resource: emit <c>[ResourceType]</c>, a handler,
    /// and a Program.cs registration. False for a group's abstract parent base.
    /// </summary>
    public bool EmitResourceType { get; init; } = true;

    /// <summary>Whether the emitted class is <c>abstract</c> (the group's parent base).</summary>
    public bool IsAbstract { get; init; }

    /// <summary>The namespace/folder segment under <c>Models</c> for this resource.</summary>
    public string ModelNamespaceSegment => Group ?? Name;

    /// <summary>The resolved base class name (<see cref="BaseTypeName"/> or the default identifiers class).</summary>
    public string EffectiveBaseType => BaseTypeName ?? Name + "Identifiers";

    /// <summary>The resolved identifiers class name used by the handler.</summary>
    public string EffectiveIdentifiersType => IdentifiersTypeName ?? Name + "Identifiers";

    /// <summary>Heading title for documentation.</summary>
    public string? Summary { get; init; }

    /// <summary>Heading description for documentation.</summary>
    public string? Description { get; init; }

    /// <summary>All properties (identifiers + writable + read-only outputs).</summary>
    public List<PropertyModel> Properties { get; } = [];

    /// <summary>Subset of <see cref="Properties"/> that are resource identifiers.</summary>
    public IEnumerable<PropertyModel> Identifiers => Properties.Where(p => p.IsIdentifier);

    /// <summary>Writable, non-identifier, non-read-only properties.</summary>
    public IEnumerable<PropertyModel> Writable => Properties.Where(p => !p.IsIdentifier && !p.IsReadOnly);

    /// <summary>Read-only output properties populated by the handler.</summary>
    public IEnumerable<PropertyModel> Outputs => Properties.Where(p => p.IsReadOnly);

    /// <summary>The CRUD operations wired for this resource.</summary>
    public CrudOps Ops { get; init; } = new();
}

/// <summary>The concrete REST operations mapped onto CRUD semantics for a resource.</summary>
public sealed class CrudOps
{
    /// <summary>POST on the collection path.</summary>
    public OperationModel? Create { get; set; }

    /// <summary>GET on the item path.</summary>
    public OperationModel? Read { get; set; }

    /// <summary>PUT or PATCH on the item path.</summary>
    public OperationModel? Update { get; set; }

    /// <summary>DELETE on the item path.</summary>
    public OperationModel? Delete { get; set; }
}

/// <summary>A single REST operation: an HTTP method against a relative path template.</summary>
public sealed class OperationModel
{
    /// <summary>HTTP method name in PascalCase: Get, Post, Put, Patch, Delete.</summary>
    public required string Method { get; init; }

    /// <summary>Relative path template without a leading slash, e.g. "pets/{petId}".</summary>
    public required string PathTemplate { get; init; }
}
