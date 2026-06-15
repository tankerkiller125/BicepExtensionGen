namespace Tankerkiller125.BicepExtensionGen.Model;

/// <summary>A C# enum generated from an OpenAPI string enum.</summary>
public sealed class EnumModel
{
    public required string Name { get; init; }

    /// <summary>PascalCase enum member names.</summary>
    public required List<string> Values { get; init; }

    /// <summary>Original JSON enum string values, index-aligned with <see cref="Values"/>.</summary>
    public required List<string> JsonValues { get; init; }
}

/// <summary>A C# class generated from a nested/object schema referenced by resources.</summary>
public sealed class NestedTypeModel
{
    public required string Name { get; init; }

    public List<PropertyModel> Properties { get; } = [];
}
