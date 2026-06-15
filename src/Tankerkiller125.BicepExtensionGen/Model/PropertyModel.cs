namespace Tankerkiller125.BicepExtensionGen.Model;

/// <summary>A single property on a resource, identifiers class, or nested type.</summary>
public sealed record PropertyModel
{
    /// <summary>PascalCase C# property name, e.g. "DisplayName".</summary>
    public required string Name { get; init; }

    /// <summary>Original JSON property name, e.g. "displayName".</summary>
    public required string JsonName { get; init; }

    /// <summary>The resolved C# type.</summary>
    public required CSharpType Type { get; init; }

    /// <summary>Documentation description (from the schema).</summary>
    public string? Description { get; init; }

    /// <summary>Whether the property is required (renders the <c>required</c> modifier).</summary>
    public bool IsRequired { get; init; }

    /// <summary>Whether the property is a read-only output (OpenAPI <c>readOnly: true</c>).</summary>
    public bool IsReadOnly { get; init; }

    /// <summary>Whether the property is a resource identifier (path parameter).</summary>
    public bool IsIdentifier { get; init; }

    /// <summary>
    /// For identifiers, the original path-parameter token (e.g. "petId") that this
    /// property substitutes into item-path templates. Null for non-identifiers.
    /// </summary>
    public string? PathParamName { get; init; }

    /// <summary>
    /// True when this property is declared on a base class the resource inherits (a variant
    /// resource's shared fields, outputs, and identifiers live on its parent base). Such
    /// properties are still visible to handlers and examples but are not re-emitted as members
    /// on the derived resource class.
    /// </summary>
    public bool Inherited { get; init; }

    /// <summary>The C# type rendered with a trailing <c>?</c> when the property is nullable.</summary>
    public string RenderedType => (IsRequired && !IsReadOnly) ? Type.Name : Type.Name + "?";
}

/// <summary>A resolved C# type plus the metadata needed to synthesize example values.</summary>
public sealed class CSharpType
{
    /// <summary>The C# type expression without nullable annotation, e.g. "List&lt;string&gt;".</summary>
    public required string Name { get; init; }

    public bool IsEnum { get; init; }

    /// <summary>Kind used by the example generator to emit a representative literal.</summary>
    public ExampleValueKind ExampleKind { get; init; } = ExampleValueKind.String;

    /// <summary>For enums: the first declared value, used in examples.</summary>
    public string? EnumFirstValue { get; init; }

    /// <summary>For arrays: the example literal of a single element.</summary>
    public string? ElementSample { get; init; }
}

public enum ExampleValueKind
{
    String,
    Int,
    Number,
    Bool,
    Enum,
    Array,
    Object,
}
