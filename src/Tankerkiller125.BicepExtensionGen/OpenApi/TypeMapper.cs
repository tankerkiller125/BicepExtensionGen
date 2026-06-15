using Microsoft.OpenApi;
using Tankerkiller125.BicepExtensionGen.Generation;
using Tankerkiller125.BicepExtensionGen.Model;

namespace Tankerkiller125.BicepExtensionGen.OpenApi;

/// <summary>
/// Maps OpenAPI schemas onto C# types, registering shared enums and nested object
/// types into the <see cref="ExtensionModel"/> as it goes. Handles <c>$ref</c>
/// (including cyclic references), <c>allOf</c> merging, arrays, enums, and free-form objects.
/// </summary>
public sealed class TypeMapper
{
    private const int MaxDepth = 100;

    private readonly ExtensionModel _model;

    // Cycle breaking + deduplication, all keyed independently of property population state.
    private readonly Dictionary<string, CSharpType> _nestedByRef = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CSharpType> _enumByRef = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CSharpType> _enumBySignature = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usedTypeNames = new(StringComparer.Ordinal);
    private int _depth;

    public TypeMapper(ExtensionModel model)
    {
        _model = model;
        // Pre-reserve BCL type names so generated enums/nested types never shadow them.
        _usedTypeNames.UnionWith(NameUtil.ReservedTypeNames);
    }

    /// <summary>Resolves the C# type for a schema, using <paramref name="suggestedName"/> for any generated enum/class.</summary>
    public CSharpType Map(IOpenApiSchema? schema, string suggestedName)
    {
        if (schema is null)
            return ObjectFallback();

        // A $ref gets a stable name and a stable identity used to break reference cycles.
        string? refId = null;
        if (schema is OpenApiSchemaReference reference && !string.IsNullOrEmpty(reference.Reference?.Id))
        {
            refId = reference.Reference!.Id!;
            suggestedName = NameUtil.Pascal(refId);
        }

        if (_depth >= MaxDepth)
        {
            _model.Warnings.Add($"Schema nesting exceeded {MaxDepth} levels at '{suggestedName}'; mapped to a free-form object.");
            return ObjectFallback();
        }

        _depth++;
        try
        {
            // String enums.
            if (schema.Enum is { Count: > 0 } && BaseType(schema) is JsonSchemaType.String or null)
                return MapEnum(schema, suggestedName, refId);

            switch (BaseType(schema))
            {
                case JsonSchemaType.String:
                    return Primitive("string", ExampleValueKind.String);
                case JsonSchemaType.Integer:
                    return Primitive(schema.Format == "int64" ? "long" : "int", ExampleValueKind.Int);
                case JsonSchemaType.Number:
                    return Primitive(schema.Format == "float" ? "float" : "double", ExampleValueKind.Number);
                case JsonSchemaType.Boolean:
                    return Primitive("bool", ExampleValueKind.Bool);
                case JsonSchemaType.Array:
                {
                    var element = Map(schema.Items, Singular(suggestedName));
                    return new CSharpType
                    {
                        Name = $"List<{element.Name}>",
                        ExampleKind = ExampleValueKind.Array,
                        ElementSample = element.Name,
                    };
                }
            }

            // Object-like (explicit object, allOf composition, or untyped-with-properties).
            if (HasObjectShape(schema))
                return MapObject(schema, suggestedName, refId);

            if (schema.OneOf is { Count: > 0 } || schema.AnyOf is { Count: > 0 })
            {
                _model.Warnings.Add($"Schema '{suggestedName}' uses oneOf/anyOf; mapped to a free-form object.");
                return ObjectFallback();
            }

            return ObjectFallback();
        }
        finally
        {
            _depth--;
        }
    }

    /// <summary>
    /// Builds the property list for an object schema, merging <c>allOf</c> members.
    /// Used for resource schemas and nested types alike.
    /// </summary>
    public List<PropertyModel> BuildObjectProperties(IOpenApiSchema schema, string ownerName) =>
        BuildProperties(ownerName, EffectiveProperties(schema), EffectiveRequired(schema));

    /// <summary>
    /// Builds property models from an explicit set of (jsonName, schema) pairs. Lets callers map a
    /// subset of an object's properties (e.g. only a union variant's variant-specific fields) without
    /// re-mapping — and re-registering nested types for — the shared fields handled elsewhere.
    /// </summary>
    public List<PropertyModel> BuildProperties(
        string ownerName,
        IEnumerable<KeyValuePair<string, IOpenApiSchema>> properties,
        ISet<string> required)
    {
        var result = new List<PropertyModel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (jsonName, propSchema) in properties)
        {
            if (!seen.Add(jsonName))
                continue;

            var name = NameUtil.Pascal(jsonName);
            if (string.Equals(name, ownerName, StringComparison.Ordinal))
                name += "Value"; // avoid CS0542 member-named-like-enclosing-type

            result.Add(new PropertyModel
            {
                Name = name,
                JsonName = jsonName,
                Type = Map(propSchema, ownerName + name),
                Description = propSchema.Description,
                IsRequired = required.Contains(jsonName),
                IsReadOnly = propSchema.ReadOnly,
            });
        }

        return result;
    }

    private CSharpType MapObject(IOpenApiSchema schema, string suggestedName, string? refId)
    {
        // Free-form maps (additionalProperties without declared properties).
        if (!EffectiveProperties(schema).Any())
        {
            if (schema.AdditionalProperties is { } ap)
            {
                var value = Map(ap, suggestedName + "Value");
                return new CSharpType { Name = $"Dictionary<string, {value.Name}>", ExampleKind = ExampleValueKind.Object };
            }

            return ObjectFallback();
        }

        // Return the already-assigned type for a known $ref, breaking reference cycles.
        if (refId is not null && _nestedByRef.TryGetValue(refId, out var existing))
            return existing;

        var name = UniqueName(NameUtil.Pascal(suggestedName));
        var type = new CSharpType { Name = name, ExampleKind = ExampleValueKind.Object };

        // Reserve BEFORE recursing so a self/mutual reference resolves to this same type.
        if (refId is not null)
            _nestedByRef[refId] = type;

        var nested = new NestedTypeModel { Name = name };
        _model.NestedTypes.Add(nested);
        nested.Properties.AddRange(BuildObjectProperties(schema, name));
        return type;
    }

    private CSharpType MapEnum(IOpenApiSchema schema, string suggestedName, string? refId)
    {
        if (refId is not null && _enumByRef.TryGetValue(refId, out var byRef))
            return byRef;

        var jsonValues = schema.Enum!
            .Select(v => v?.ToString() ?? "")
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Reuse an identical enum (same values) regardless of where it was declared.
        var signature = string.Join("|", jsonValues);
        if (_enumBySignature.TryGetValue(signature, out var bySig))
        {
            if (refId is not null)
                _enumByRef[refId] = bySig;
            return bySig;
        }

        var model = new EnumModel
        {
            Name = UniqueName(NameUtil.Pascal(suggestedName)),
            Values = UniqueMemberNames(jsonValues),
            JsonValues = jsonValues,
        };
        _model.Enums.Add(model);

        var type = EnumType(model);
        _enumBySignature[signature] = type;
        if (refId is not null)
            _enumByRef[refId] = type;
        return type;
    }

    private static CSharpType EnumType(EnumModel model) => new()
    {
        Name = model.Name,
        IsEnum = true,
        ExampleKind = ExampleValueKind.Enum,
        EnumFirstValue = model.JsonValues.FirstOrDefault(),
    };

    /// <summary>Produces distinct PascalCase enum member names, suffixing collisions.</summary>
    private static List<string> UniqueMemberNames(List<string> jsonValues)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>(jsonValues.Count);
        foreach (var value in jsonValues)
        {
            var member = NameUtil.Pascal(value);
            if (string.IsNullOrEmpty(member))
                member = "Value";

            var unique = member;
            var suffix = 2;
            while (!used.Add(unique))
                unique = member + suffix++;
            result.Add(unique);
        }

        return result;
    }

    /// <summary>Returns a type name unique across all generated enums and nested types.</summary>
    private string UniqueName(string baseName)
    {
        if (string.IsNullOrEmpty(baseName))
            baseName = "Type";

        var name = baseName;
        var suffix = 2;
        while (!_usedTypeNames.Add(name))
            name = baseName + suffix++;
        return name;
    }

    // ---- schema helpers -------------------------------------------------

    private static CSharpType Primitive(string name, ExampleValueKind kind) => new() { Name = name, ExampleKind = kind };

    private static CSharpType ObjectFallback() => new() { Name = "Dictionary<string, object>", ExampleKind = ExampleValueKind.Object };

    /// <summary>The schema's declared type with the nullable <c>Null</c> flag removed.</summary>
    private static JsonSchemaType? BaseType(IOpenApiSchema schema)
    {
        if (schema.Type is not { } t)
            return null;
        var stripped = t & ~JsonSchemaType.Null;
        return stripped == 0 ? null : stripped;
    }

    private static bool HasObjectShape(IOpenApiSchema schema) =>
        BaseType(schema) == JsonSchemaType.Object
        || schema.AllOf is { Count: > 0 }
        || schema.Properties is { Count: > 0 }
        || schema.AdditionalProperties is not null;

    /// <summary>Enumerates an object's properties, flattening <c>allOf</c> members.</summary>
    public static IEnumerable<KeyValuePair<string, IOpenApiSchema>> EffectiveProperties(IOpenApiSchema schema)
    {
        if (schema.Properties is { })
            foreach (var p in schema.Properties)
                yield return p;

        if (schema.AllOf is { })
            foreach (var sub in schema.AllOf)
                foreach (var p in EffectiveProperties(sub))
                    yield return p;
    }

    /// <summary>The union of required property names across the schema and its <c>allOf</c> members.</summary>
    public static HashSet<string> EffectiveRequired(IOpenApiSchema schema)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (schema.Required is { })
            set.UnionWith(schema.Required);
        if (schema.AllOf is { })
            foreach (var sub in schema.AllOf)
                set.UnionWith(EffectiveRequired(sub));
        return set;
    }

    private static string Singular(string name) => NameUtil.SingularPascal(name);
}
