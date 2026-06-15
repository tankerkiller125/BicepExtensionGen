using System.Text;
using Tankerkiller125.BicepExtensionGen.Model;

namespace Tankerkiller125.BicepExtensionGen.Generation;

/// <summary>Synthesizes representative Bicep snippets used in <c>[BicepDocExample]</c> and the README.</summary>
public static class ExampleGenerator
{
    /// <summary>
    /// A Bicep <c>resource</c> declaration setting identifiers and required writable properties.
    /// <paramref name="resourceType"/> overrides the type string (e.g. a prefix-qualified name).
    /// </summary>
    public static string ResourceDeclaration(ResourceModel resource, string? resourceType = null)
    {
        var sb = new StringBuilder();
        var varName = NameUtil.Camel(resource.Name).TrimStart('@');
        sb.Append("resource ").Append(varName).Append(" '").Append(resourceType ?? resource.ResourceTypeName).Append("' = {\n");

        foreach (var p in resource.Identifiers.Concat(resource.Writable.Where(w => w.IsRequired)))
            sb.Append("  ").Append(p.JsonName).Append(": ").Append(SampleValue(p)).Append('\n');

        sb.Append("}\n");
        return sb.ToString();
    }

    /// <summary>A representative Bicep literal for a single property.</summary>
    public static string SampleValue(PropertyModel p) => p.Type.ExampleKind switch
    {
        ExampleValueKind.String => $"'example-{p.JsonName}'",
        ExampleValueKind.Int => "1",
        ExampleValueKind.Number => "1.0",
        ExampleValueKind.Bool => "true",
        ExampleValueKind.Enum => $"'{NameUtil.Pascal(p.Type.EnumFirstValue ?? "Value")}'",
        ExampleValueKind.Array => "[]",
        ExampleValueKind.Object => "{}",
        _ => "'value'",
    };
}
