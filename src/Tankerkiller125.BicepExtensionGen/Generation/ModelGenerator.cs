using Tankerkiller125.BicepExtensionGen.Model;

namespace Tankerkiller125.BicepExtensionGen.Generation;

/// <summary>Emits the annotated C# model classes (resources, identifiers, enums, nested types).</summary>
public static class ModelGenerator
{
    private static readonly string[] ModelUsings =
    [
        "using System.Text.Json.Serialization;",
        "using Azure.Bicep.Types.Concrete;",
        "using Bicep.Local.Extension.Types.Attributes;",
        "using Bicep.LocalDeploy;",
    ];

    /// <summary>Generates the <c>Models/&lt;Resource&gt;/&lt;Resource&gt;.cs</c> file for one resource.</summary>
    public static string ResourceFile(ExtensionModel ext, ResourceModel r, string rootNs)
    {
        var w = new CodeWriter();
        foreach (var u in ModelUsings)
            w.Line(u);
        // Shared enums/nested types live in a sibling namespace to avoid clashing with this
        // resource's own sub-namespace (e.g. resource "GroupMember" vs a nested type "GroupMember").
        w.Line($"using {rootNs}.Models.Common;");
        w.Blank();
        w.Line($"namespace {rootNs}.Models.{r.ModelNamespaceSegment};");
        w.Blank();

        // The abstract parent base of a variant group carries shared fields but is not itself a resource.
        if (r.EmitResourceType)
        {
            WriteResourceDocAttributes(w, ext, r);
            w.Line($"[ResourceType(\"{ext.QualifiedResourceType(r)}\")]");
        }

        var classKeyword = r.IsAbstract ? "public abstract class" : "public class";
        using (w.Block($"{classKeyword} {r.Name} : {r.EffectiveBaseType}"))
        {
            // Inherited properties (a variant's shared fields/outputs/identifiers) live on the base.
            foreach (var p in r.Properties.Where(p => !p.IsIdentifier && !p.Inherited))
                WriteProperty(w, p);
        }

        // Identifiers class (separate, as required by the typed handler base); variants inherit it.
        if (r.OwnsIdentifiers)
        {
            w.Blank();
            using (w.Block($"public class {r.EffectiveIdentifiersType}"))
            {
                foreach (var p in r.Identifiers)
                    WriteProperty(w, p);
            }
        }

        return w.ToString();
    }

    /// <summary>
    /// Generates <c>Models/Common.cs</c> with shared enums and nested types. Always emitted (even
    /// when empty) so the <c>Models.Common</c> namespace that resource files import always exists.
    /// </summary>
    public static string CommonFile(ExtensionModel ext, string rootNs)
    {
        var w = new CodeWriter();
        foreach (var u in ModelUsings)
            w.Line(u);
        w.Blank();
        w.Line($"namespace {rootNs}.Models.Common;");

        foreach (var e in ext.Enums)
        {
            w.Blank();
            using (w.Block($"public enum {e.Name}"))
            {
                for (var i = 0; i < e.Values.Count; i++)
                {
                    if (!string.Equals(e.Values[i], e.JsonValues[i], StringComparison.Ordinal))
                        w.Line($"[JsonStringEnumMemberName({Literal(e.JsonValues[i])})]");
                    w.Line($"{e.Values[i]},");
                }
            }
        }

        foreach (var t in ext.NestedTypes)
        {
            w.Blank();
            using (w.Block($"public class {t.Name}"))
            {
                foreach (var p in t.Properties)
                    WriteProperty(w, p);
            }
        }

        return w.ToString();
    }

    /// <summary>Writes the documentation attributes consumed by bicep-local-docgen.</summary>
    private static void WriteResourceDocAttributes(CodeWriter w, ExtensionModel ext, ResourceModel r)
    {
        if (!string.IsNullOrWhiteSpace(r.Category))
            w.Line($"[BicepFrontMatter(\"category\", {Literal(r.Category!)})]");

        var heading = Escape(r.Summary ?? r.Name);
        var headingDesc = Escape(r.Description ?? $"Manages {r.Name} resources.");
        w.Line($"[BicepDocHeading(\"{heading}\", \"{headingDesc}\")]");

        var example = ExampleGenerator.ResourceDeclaration(r, ext.QualifiedResourceType(r)).Replace("\"", "\"\"");
        w.Line("[BicepDocExample(");
        w.Line($"    \"Create a {r.Name}\",");
        w.Line($"    \"Creates a {r.Name} resource with its required properties.\",");
        // The example is a verbatim @-string; emit it raw so its own newlines are preserved.
        w.Raw($"    @\"{example}\"\n");
        w.Line(")]");
    }

    private static void WriteProperty(CodeWriter w, PropertyModel p)
    {
        var description = Escape(p.Description ?? $"The {p.Name} value.");
        w.Line($"[TypeProperty(\"{description}\"{FlagsArg(p)})]");
        w.Line($"[JsonPropertyName({Literal(p.JsonName)})]");
        if (p.Type.IsEnum)
            w.Line("[JsonConverter(typeof(JsonStringEnumConverter))]");

        var modifier = p is { IsRequired: true, IsReadOnly: false } ? "required " : "";
        w.Line($"public {modifier}{p.RenderedType} {p.Name} {{ get; set; }}");
        w.Blank();
    }

    private static string FlagsArg(PropertyModel p)
    {
        if (p.IsIdentifier)
            return ", ObjectTypePropertyFlags.Identifier | ObjectTypePropertyFlags.Required";
        if (p.IsReadOnly)
            return ", ObjectTypePropertyFlags.ReadOnly";
        if (p.IsRequired)
            return ", ObjectTypePropertyFlags.Required";
        return "";
    }

    /// <summary>Escapes a string for use inside a regular C# "double-quoted" literal.</summary>
    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ").Trim();

    private static string Literal(string value) => "\"" + Escape(value) + "\"";
}
