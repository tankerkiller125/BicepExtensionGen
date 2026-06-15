using System.Text.RegularExpressions;
using Microsoft.OpenApi;
using Tankerkiller125.BicepExtensionGen.Generation;
using Tankerkiller125.BicepExtensionGen.Model;

namespace Tankerkiller125.BicepExtensionGen.OpenApi;

/// <summary>How resources are derived from the OpenAPI document.</summary>
public enum MappingMode
{
    /// <summary>Pair collection/item paths and infer CRUD from HTTP verbs (default).</summary>
    Path,

    /// <summary>One resource per component object schema (best-effort wiring).</summary>
    Schema,
}

/// <summary>Builds an <see cref="ExtensionModel"/> from a parsed OpenAPI document.</summary>
public sealed partial class ResourceModelBuilder
{
    private static readonly Regex PathParam = ParamRegex();

    private readonly OpenApiDocument _doc;
    private readonly string _name;
    private readonly string _version;
    private readonly MappingMode _mode;
    private readonly ExclusionFilter _filter;
    private readonly string? _resourceTypePrefix;
    private readonly bool _useResourceTypePath;

    // Resource names assigned to variant-group members, reserved so standalone names don't collide.
    private readonly HashSet<string> _groupNames = new(StringComparer.Ordinal);

    public ResourceModelBuilder(
        OpenApiDocument doc,
        string name,
        string version,
        MappingMode mode,
        ExclusionFilter? filter = null,
        string? resourceTypePrefix = null,
        bool useResourceTypePath = false)
    {
        _doc = doc;
        _name = name;
        _version = version;
        _mode = mode;
        _filter = filter ?? ExclusionFilter.None;
        _resourceTypePrefix = string.IsNullOrWhiteSpace(resourceTypePrefix) ? null : resourceTypePrefix.Trim().TrimEnd('/');
        _useResourceTypePath = useResourceTypePath;
    }

    public ExtensionModel Build()
    {
        var model = new ExtensionModel
        {
            Name = _name,
            AssemblyName = NameUtil.AssemblyName(_name),
            Version = _version,
            BaseUrl = _doc.Servers?.FirstOrDefault()?.Url,
            ResourceTypePrefix = _resourceTypePrefix,
            UseResourceTypePath = _useResourceTypePath,
        };
        BuildAuth(model);

        var mapper = new TypeMapper(model);

        var resources = _mode == MappingMode.Schema
            ? BuildFromSchemas(mapper, model)
            : BuildFromPaths(mapper, model);

        if (resources.Count == 0 && _mode == MappingMode.Path)
        {
            model.Warnings.Add("No collection/item path pairs were found; falling back to schema-based mapping.");
            resources = BuildFromSchemas(mapper, model);
        }

        AssignNames(resources, model);
        if (_useResourceTypePath)
            EnsureUniqueTypePaths(resources);
        model.Resources.AddRange(resources);

        if (model.Resources.Count == 0)
            model.Warnings.Add("No resources could be derived from the OpenAPI document.");

        return model;
    }

    // ---- path/CRUD inference -------------------------------------------

    private List<ResourceModel> BuildFromPaths(TypeMapper mapper, ExtensionModel model)
    {
        var resources = new List<ResourceModel>();
        var paths = IncludedPaths();
        if (paths.Count == 0)
            return resources;

        var consumedCollections = new HashSet<string>(StringComparer.Ordinal);

        // Item paths (ending in "/{param}") drive resource discovery.
        foreach (var (itemPath, itemDef) in paths.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!EndsWithParam(itemPath))
                continue;

            var collectionPath = StripTrailingParam(itemPath);
            var segment = LastNonParamSegment(collectionPath);
            if (segment is null)
                continue;

            paths.TryGetValue(collectionPath, out var collectionDef);
            if (collectionDef is not null)
                consumedCollections.Add(collectionPath);

            resources.AddRange(BuildResource(mapper, model, segment, collectionPath, itemPath, collectionDef, itemDef));
        }

        // Collection-only paths with a POST but no item sibling (create-only resources).
        foreach (var (path, def) in paths.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (EndsWithParam(path) || consumedCollections.Contains(path))
                continue;
            if (GetOp(def, HttpMethod.Post) is null)
                continue;

            var segment = LastNonParamSegment(path);
            if (segment is null)
                continue;

            resources.AddRange(BuildResource(mapper, model, segment, path, itemPath: null, def, itemDef: null));
        }

        return resources;
    }

    private IReadOnlyList<ResourceModel> BuildResource(
        TypeMapper mapper,
        ExtensionModel model,
        string segment,
        string collectionPath,
        string? itemPath,
        IOpenApiPathItem? collectionDef,
        IOpenApiPathItem? itemDef)
    {
        var segments = SegmentsOf(collectionPath);
        var resourceName = segments.Count > 0 ? segments[^1] : NameUtil.SingularPascal(segment);

        var createOp = GetOp(collectionDef, HttpMethod.Post);
        var readOp = GetOp(itemDef, HttpMethod.Get);
        var updateOp = GetOp(itemDef, HttpMethod.Put) ?? GetOp(itemDef, HttpMethod.Patch);
        var deleteOp = GetOp(itemDef, HttpMethod.Delete);

        var requestSchema = RequestSchema(createOp) ?? RequestSchema(updateOp);
        // Cloudflare (and similar) wrap responses in a { result, success, errors, messages } envelope;
        // descend into `result` so the resource shape is the actual object, not the wrapper.
        var responseSchema = UnwrapEnvelope(ResponseSchema(readOp) ?? ResponseSchema(createOp) ?? CollectionItemSchema(GetOp(collectionDef, HttpMethod.Get)));

        var ops = new CrudOps
        {
            Create = createOp is null ? null : new OperationModel { Method = "Post", PathTemplate = Relative(collectionPath) },
            Read = readOp is null ? null : new OperationModel { Method = "Get", PathTemplate = Relative(itemPath!) },
            Update = updateOp is null ? null : new OperationModel
            {
                Method = GetOp(itemDef, HttpMethod.Put) is not null ? "Put" : "Patch",
                PathTemplate = Relative(itemPath!),
            },
            Delete = deleteOp is null ? null : new OperationModel { Method = "Delete", PathTemplate = Relative(itemPath!) },
        };

        var tag = (createOp ?? readOp ?? updateOp ?? deleteOp)?.Tags?.FirstOrDefault()?.Reference?.Id;
        var idPath = itemPath ?? collectionPath;

        // A discriminated-union *write* body (e.g. a DNS record's per-type variants) becomes one
        // resource per variant, grouped under a shared abstract parent base. Only the request body
        // drives this: a response-only union is a read shape (e.g. a status that is completed /
        // in-progress / not-found), not something a user authors, so it stays a single resource.
        // The union is also only split when every variant can be named distinctly (by a discriminator
        // value or a named $ref); otherwise the variants would be meaningless "Variant1/2/3".
        if (requestSchema is not null)
        {
            var variants = CollectUnionVariants(requestSchema);
            if (variants.Count >= 2)
            {
                var discriminator = (responseSchema?.Discriminator ?? requestSchema.Discriminator)?.PropertyName ?? "type";
                if (variants.All(v => VariantSuffix(v, discriminator) is not null))
                {
                    var groupDescription = responseSchema?.Description ?? requestSchema.Description
                        ?? (readOp ?? createOp)?.Description ?? (readOp ?? createOp)?.Summary;
                    return BuildVariantGroup(mapper, model, resourceName, segments, ops, tag, groupDescription, idPath, variants, responseSchema, requestSchema, discriminator);
                }

                model.Warnings.Add($"Union request body for '{resourceName}' has variants that cannot be named distinctly; emitted as a single resource.");
            }
        }

        var shape = responseSchema ?? requestSchema;
        var properties = BuildProperties(mapper, resourceName, shape, requestSchema);
        ApplyIdentifiers(properties, idPath, resourceName);

        // Use the resource name as the heading; prefer schema/operation prose for the description.
        var description = shape?.Description
            ?? (readOp ?? createOp)?.Description
            ?? (readOp ?? createOp)?.Summary;

        var resource = new ResourceModel
        {
            Name = resourceName,
            ResourceTypeName = resourceName,
            NameSegments = segments,
            TypePath = segments,
            Category = tag,
            Summary = null,
            Description = description,
            Ops = ops,
        };
        resource.Properties.AddRange(properties);
        return [resource];
    }

    // ---- discriminated-union variant groups ----------------------------

    /// <summary>
    /// Builds a group for a discriminated-union resource: an abstract parent base carrying the
    /// fields shared by every variant (plus read-only outputs and identifiers), and one concrete
    /// resource per variant that derives from the base and adds only its variant-specific fields.
    /// All share a <c>Models/&lt;Parent&gt;/</c> folder and namespace.
    /// </summary>
    private List<ResourceModel> BuildVariantGroup(
        TypeMapper mapper,
        ExtensionModel model,
        string parentName,
        List<string> segments,
        CrudOps ops,
        string? tag,
        string? description,
        string idPath,
        List<IOpenApiSchema> variants,
        IOpenApiSchema? responseRecord,
        IOpenApiSchema requestUnion,
        string discriminator)
    {
        parentName = UniqueResourceName(parentName, model);

        // Fields structurally shared by every variant (common $ref allOf members, e.g. "shared-fields").
        var sharedMembers = CommonRefMembers(variants);
        var sharedPairs = DistinctByKey(sharedMembers.SelectMany(TypeMapper.EffectiveProperties));
        var sharedNames = sharedPairs.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);

        // Read-only outputs and the required set come from the response record (when present).
        var required = TypeMapper.EffectiveRequired(responseRecord ?? requestUnion);
        var metadataPairs = responseRecord is null
            ? []
            : DistinctByKey(TypeMapper.EffectiveProperties(responseRecord)
                .Where(p => !sharedNames.Contains(p.Key) && !string.Equals(p.Key, discriminator, StringComparison.Ordinal)));

        // ---- parent base: shared writable fields + outputs + identifiers ----
        var baseProps = new List<PropertyModel>();
        baseProps.AddRange(Finalize(mapper.BuildProperties(parentName, sharedPairs, required), required, forceReadOnly: false));
        baseProps.AddRange(Finalize(mapper.BuildProperties(parentName, metadataPairs, required), required, forceReadOnly: true));
        ApplyIdentifiers(baseProps, idPath, parentName);

        var identifiersType = parentName + "Identifiers";
        var baseResource = new ResourceModel
        {
            Name = parentName,
            ResourceTypeName = parentName,
            NameSegments = segments,
            TypePath = segments,
            Category = tag,
            Description = description,
            Ops = new CrudOps(),
            Group = parentName,
            EmitResourceType = false,
            IsAbstract = true,
            OwnsIdentifiers = true,
            BaseTypeName = identifiersType,
            IdentifiersTypeName = identifiersType,
        };
        baseResource.Properties.AddRange(baseProps);

        var results = new List<ResourceModel> { baseResource };

        // Properties inherited by every variant from the base (handlers/examples still see them).
        var inherited = baseProps.Select(p => p with { Inherited = true }).ToList();

        foreach (var variant in variants)
        {
            var suffix = VariantSuffix(variant, discriminator)!; // non-null: callers gate on this
            var variantName = UniqueResourceName(parentName + suffix, model);

            var variantPairs = DistinctByKey(TypeMapper.EffectiveProperties(variant)
                .Where(p => !sharedNames.Contains(p.Key)));
            var variantProps = Finalize(mapper.BuildProperties(variantName, variantPairs, required), required, forceReadOnly: false);

            var variantDescription = variant.Description ?? variant.Title
                ?? (description is null ? null : $"{description} ({suffix} record).");

            var resource = new ResourceModel
            {
                Name = variantName,
                ResourceTypeName = variantName,
                NameSegments = segments,
                // The discriminator value becomes a child segment: zones/dnsRecords/A.
                TypePath = [.. segments, suffix],
                Category = tag,
                Description = variantDescription,
                Ops = ops,
                Group = parentName,
                BaseTypeName = parentName,
                IdentifiersTypeName = identifiersType,
                OwnsIdentifiers = false,
            };
            resource.Properties.AddRange(inherited);
            resource.Properties.AddRange(variantProps);
            results.Add(resource);
        }

        return results;
    }

    /// <summary>Applies the writable/readonly/required reconciliation used across resource property sets.</summary>
    private static List<PropertyModel> Finalize(List<PropertyModel> props, ISet<string> required, bool forceReadOnly)
    {
        for (var i = 0; i < props.Count; i++)
        {
            var p = props[i];
            var isReadOnly = forceReadOnly || p.IsReadOnly;
            props[i] = p with
            {
                IsReadOnly = isReadOnly,
                IsRequired = required.Contains(p.JsonName) && !isReadOnly,
            };
        }

        return props;
    }

    private static List<KeyValuePair<string, IOpenApiSchema>> DistinctByKey(IEnumerable<KeyValuePair<string, IOpenApiSchema>> pairs)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<KeyValuePair<string, IOpenApiSchema>>();
        foreach (var p in pairs)
            if (seen.Add(p.Key))
                result.Add(p);
        return result;
    }

    /// <summary>The $ref'd allOf member schemas shared by every variant (the union's common base types).</summary>
    private static List<IOpenApiSchema> CommonRefMembers(List<IOpenApiSchema> variants)
    {
        static Dictionary<string, IOpenApiSchema> RefMembers(IOpenApiSchema schema)
        {
            var map = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
            if (schema.AllOf is { } allOf)
                foreach (var m in allOf)
                    if (m is OpenApiSchemaReference { Reference.Id: { Length: > 0 } id })
                        map[id] = m;
            return map;
        }

        var perVariant = variants.Select(RefMembers).ToList();
        if (perVariant.Count == 0)
            return [];

        var commonIds = perVariant.Select(m => (IEnumerable<string>)m.Keys)
            .Aggregate((a, b) => a.Intersect(b, StringComparer.Ordinal))
            .ToList();

        return commonIds.Select(id => perVariant[0][id]).ToList();
    }

    /// <summary>
    /// A distinct name suffix for a union variant, taken from its discriminator value (a single-value
    /// enum) or, failing that, its schema <c>$ref</c> name. Returns <c>null</c> when the variant is an
    /// anonymous inline schema with neither — such a union can't be split into meaningfully-named
    /// resources and is left as a single resource instead.
    /// </summary>
    private static string? VariantSuffix(IOpenApiSchema variant, string discriminator)
    {
        var prop = TypeMapper.EffectiveProperties(variant)
            .FirstOrDefault(p => string.Equals(p.Key, discriminator, StringComparison.Ordinal)).Value;

        var value = prop?.Enum is { Count: 1 } e ? e[0]?.ToString() : null;
        value ??= (variant as OpenApiSchemaReference)?.Reference?.Id;
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var letters = value.Where(char.IsLetterOrDigit).ToArray();
        if (letters.Length == 0)
            return null;

        // Preserve an all-caps record type (A, CNAME, TXT); otherwise PascalCase with the word
        // boundaries in the raw value (so "self_hosted" -> "SelfHosted", not "Selfhosted").
        return letters.All(c => !char.IsLower(c)) ? new string(letters) : NameUtil.Pascal(value);
    }

    private string UniqueResourceName(string baseName, ExtensionModel model)
    {
        var taken = new HashSet<string>(_groupNames, StringComparer.Ordinal);
        taken.UnionWith(NameUtil.ReservedTypeNames);
        taken.UnionWith(model.NestedTypes.Select(t => t.Name));
        taken.UnionWith(model.Enums.Select(e => e.Name));

        var name = baseName;
        var suffix = 2;
        while (!taken.Add(name))
            name = baseName + suffix++;
        _groupNames.Add(name);
        return name;
    }

    /// <summary>Flattens a oneOf/anyOf (possibly nested, possibly wrapped in allOf) into its leaf variant schemas.</summary>
    private static List<IOpenApiSchema> CollectUnionVariants(IOpenApiSchema? schema)
    {
        var leaves = new List<IOpenApiSchema>();
        if (schema is not null)
            Collect(schema, leaves, new HashSet<string>(StringComparer.Ordinal), 0);
        return leaves;

        static void Collect(IOpenApiSchema s, List<IOpenApiSchema> acc, HashSet<string> visited, int depth)
        {
            if (depth > 32)
                return;
            if (s is OpenApiSchemaReference { Reference.Id: { Length: > 0 } id } && !visited.Add(id))
                return;

            var union = s.OneOf is { Count: > 0 } ? s.OneOf
                : s.AnyOf is { Count: > 0 } ? s.AnyOf
                : null;

            if (union is not null)
            {
                foreach (var member in union)
                    Collect(member, acc, visited, depth + 1);
                return;
            }

            // A union nested under allOf alongside sibling fields (a response composite): descend
            // only the union member(s), leaving the sibling metadata to the response-record handling.
            if (s.AllOf is { Count: > 0 } allOf && allOf.Any(IsUnion))
            {
                foreach (var member in allOf.Where(IsUnion))
                    Collect(member, acc, visited, depth + 1);
                return;
            }

            acc.Add(s); // a concrete variant leaf
        }

        static bool IsUnion(IOpenApiSchema s) =>
            s.OneOf is { Count: > 0 } || s.AnyOf is { Count: > 0 }
            || (s.AllOf is { Count: > 0 } allOf && allOf.Any(m => m.OneOf is { Count: > 0 } || m.AnyOf is { Count: > 0 }));
    }

    /// <summary>
    /// Detects a result-envelope response — an object whose effective properties include a
    /// <c>result</c> alongside the usual <c>success</c>/<c>errors</c>/<c>messages</c> siblings — and
    /// returns the inner <c>result</c> schema (its element type for a collection). Otherwise returns
    /// the schema unchanged.
    /// </summary>
    private static IOpenApiSchema? UnwrapEnvelope(IOpenApiSchema? schema)
    {
        if (schema is null)
            return null;

        var props = DistinctByKey(TypeMapper.EffectiveProperties(schema))
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

        if (!props.TryGetValue("result", out var result))
            return schema;

        var enveloped = props.ContainsKey("success")
            || (props.ContainsKey("errors") && props.ContainsKey("messages"));
        if (!enveloped)
            return schema;

        // A collection envelope's result is an array; unwrap to the element schema.
        return (result.Type & ~JsonSchemaType.Null) == JsonSchemaType.Array && result.Items is { } item
            ? item
            : result;
    }

    // ---- schema-based fallback -----------------------------------------

    private List<ResourceModel> BuildFromSchemas(TypeMapper mapper, ExtensionModel model)
    {
        var resources = new List<ResourceModel>();
        var schemas = _doc.Components?.Schemas;
        if (schemas is null)
            return resources;

        foreach (var (schemaName, schema) in schemas)
        {
            if (!_filter.IsEmpty && _filter.MatchesName(schemaName))
                continue;

            if (schema.Type is { } t && (t & ~JsonSchemaType.Null) != JsonSchemaType.Object && schema.AllOf is not { Count: > 0 } && schema.Properties is not { Count: > 0 })
                continue;

            var resourceName = NameUtil.Pascal(schemaName);
            var properties = BuildProperties(mapper, resourceName, schema, requestSchema: schema);
            if (properties.Count == 0)
                continue;

            // Use an existing id/name property as the identifier, else synthesize one.
            EnsureSyntheticIdentifier(properties, resourceName);

            var resource = new ResourceModel
            {
                Name = resourceName,
                ResourceTypeName = resourceName,
                NameSegments = [resourceName],
                TypePath = [resourceName],
                Description = schema.Description,
                Ops = new CrudOps
                {
                    Create = new OperationModel { Method = "Post", PathTemplate = NameUtil.AssemblyName(resourceName) },
                },
            };
            resource.Properties.AddRange(properties);
            resources.Add(resource);
        }

        model.Warnings.Add("Schema-based mapping produced resources without concrete REST endpoints; review the generated handlers.");
        return resources;
    }

    // ---- property assembly ---------------------------------------------

    private static List<PropertyModel> BuildProperties(TypeMapper mapper, string resourceName, IOpenApiSchema? shape, IOpenApiSchema? requestSchema)
    {
        if (shape is null)
            return [];

        var writable = requestSchema is null
            ? null
            : TypeMapperProperties(requestSchema);
        var required = requestSchema is null
            ? TypeMapper.EffectiveRequired(shape)
            : TypeMapper.EffectiveRequired(requestSchema);

        var props = mapper.BuildObjectProperties(shape, resourceName);
        for (var i = 0; i < props.Count; i++)
        {
            var p = props[i];
            var isReadOnly = p.IsReadOnly || (writable is not null && !writable.Contains(p.JsonName));
            props[i] = p with
            {
                IsRequired = required.Contains(p.JsonName) && !isReadOnly,
                IsReadOnly = isReadOnly,
            };
        }

        return props;
    }

    private static HashSet<string> TypeMapperProperties(IOpenApiSchema schema)
    {
        // Mirror TypeMapper's effective-property flattening for the writable set.
        var set = new HashSet<string>(StringComparer.Ordinal);
        void Walk(IOpenApiSchema s)
        {
            if (s.Properties is { })
                foreach (var k in s.Properties.Keys)
                    set.Add(k);
            if (s.AllOf is { })
                foreach (var sub in s.AllOf)
                    Walk(sub);
        }
        Walk(schema);
        return set;
    }

    // ---- identifiers ----------------------------------------------------

    private static void ApplyIdentifiers(List<PropertyModel> properties, string path, string resourceName)
    {
        // Each path parameter, paired with the container segment that precedes it. For a nested
        // path like /zones/{zone_id}/dns_records/{dns_record_id} the parents (zone) become required
        // identifiers alongside the resource's own id, which is how Bicep local extensions model
        // containment (the child carries its parent's key).
        var pathParams = ParamsWithContainers(path);
        if (pathParams.Count == 0)
        {
            EnsureSyntheticIdentifier(properties, resourceName);
            return;
        }

        var usedNames = new HashSet<string>(properties.Select(p => p.Name), StringComparer.Ordinal);

        // Process trailing-to-leading so front-inserted identifiers end up parent-first.
        for (var k = pathParams.Count - 1; k >= 0; k--)
        {
            var (param, container) = pathParams[k];
            var isOwn = k == pathParams.Count - 1;

            // Only the resource's own key may bind to a generic "id"/"name" schema property;
            // parent keys must match by name or be synthesized, never steal the child's id.
            var idx = MatchIdentifier(properties, param, resourceName, allowGenericFallback: isOwn);
            if (idx >= 0)
            {
                properties[idx] = properties[idx] with
                {
                    IsIdentifier = true,
                    IsRequired = true,
                    IsReadOnly = false,
                    PathParamName = param,
                };
                continue;
            }

            var parent = container is null ? null : NameUtil.SingularPascal(container);
            var pascal = NameUtil.Pascal(param);

            // Promote a generic parent id ("id"/"name") to the parent's name, e.g. {id} under
            // /zones -> "ZoneId" instead of a bare, ambiguous "Id".
            var baseName = !isOwn && parent is not null && pascal is "Id" or "Name"
                ? parent + pascal
                : pascal;

            // Keep the property name unique within the resource (distinct params can pascalize to
            // the same name, e.g. "account_id" and "accountId").
            var name = baseName;
            var disambiguator = 2;
            while (!usedNames.Add(name))
                name = baseName + disambiguator++;

            var description = isOwn
                ? $"The unique identifier of the {resourceName}."
                : parent is not null
                    ? $"The identifier of the parent {parent} that contains this {resourceName}."
                    : $"Identifier for the {resourceName} resource.";

            properties.Insert(0, new PropertyModel
            {
                Name = name,
                JsonName = NameUtil.Camel(name).TrimStart('@'),
                Type = new CSharpType { Name = "string", ExampleKind = ExampleValueKind.String },
                Description = description,
                IsRequired = true,
                IsIdentifier = true,
                PathParamName = param,
            });
        }
    }

    private static int MatchIdentifier(List<PropertyModel> properties, string param, string resourceName, bool allowGenericFallback = true)
    {
        var paramPascal = NameUtil.Pascal(param);
        bool Free(PropertyModel p) => !p.IsIdentifier; // never bind two path params to one property

        // Exact match on the parameter name.
        var exact = properties.FindIndex(p => Free(p) && string.Equals(p.Name, paramPascal, StringComparison.Ordinal));
        if (exact >= 0)
            return exact;

        // "<Resource>Id" style: param "petId" matches property "id" on resource "Pet".
        var prefixed = properties.FindIndex(p => Free(p) && string.Equals(resourceName + p.Name, paramPascal, StringComparison.Ordinal));
        if (prefixed >= 0)
            return prefixed;

        // Conventional id/name fallbacks — only for the resource's own key.
        if (allowGenericFallback)
        {
            foreach (var candidate in new[] { "Id", "Name" })
            {
                var found = properties.FindIndex(p => Free(p) && string.Equals(p.Name, candidate, StringComparison.Ordinal));
                if (found >= 0)
                    return found;
            }
        }

        return -1;
    }

    private static void EnsureSyntheticIdentifier(List<PropertyModel> properties, string resourceName)
    {
        if (properties.Any(p => p.IsIdentifier))
            return;

        var idx = MatchIdentifier(properties, "id", resourceName);
        if (idx >= 0)
        {
            properties[idx] = properties[idx] with { IsIdentifier = true, IsRequired = true, IsReadOnly = false, PathParamName = "id" };
            return;
        }

        properties.Insert(0, new PropertyModel
        {
            Name = "Name",
            JsonName = "name",
            Type = new CSharpType { Name = "string", ExampleKind = ExampleValueKind.String },
            Description = $"The unique name of the {resourceName} resource.",
            IsRequired = true,
            IsIdentifier = true,
            PathParamName = "name",
        });
    }

    // ---- auth -----------------------------------------------------------

    private void BuildAuth(ExtensionModel model)
    {
        var schemes = _doc.Components?.SecuritySchemes;
        if (schemes is null || schemes.Count == 0)
            return;

        // Credential property names must be unique and not clash with BaseUrl.
        var used = new HashSet<string>(StringComparer.Ordinal) { "BaseUrl" };
        string Property(string baseName)
        {
            var name = NameUtil.Pascal(baseName);
            var unique = name;
            var suffix = 2;
            while (!used.Add(unique))
                unique = name + suffix++;
            return unique;
        }

        foreach (var (key, scheme) in schemes)
        {
            var built = MapScheme(key, scheme, Property);
            if (built is not null)
                model.SecuritySchemes.Add(built);
            else
                model.Warnings.Add($"Security scheme '{key}' ({scheme.Type}) is not supported and was skipped.");
        }
    }

    private static SecuritySchemeModel? MapScheme(string key, IOpenApiSecurityScheme scheme, Func<string, string> property)
    {
        switch (scheme.Type)
        {
            case SecuritySchemeType.ApiKey:
            {
                var paramName = scheme.Name ?? key;
                var (kind, where) = scheme.In switch
                {
                    ParameterLocation.Query => (SecurityKind.ApiKeyQuery, $"the '{paramName}' query parameter"),
                    ParameterLocation.Cookie => (SecurityKind.ApiKeyCookie, $"the '{paramName}' cookie"),
                    _ => (SecurityKind.ApiKeyHeader, $"the '{paramName}' header"),
                };
                return new SecuritySchemeModel
                {
                    Key = key,
                    Kind = kind,
                    ParameterName = paramName,
                    PrimaryProperty = property(key),
                    Description = $"API key for the '{key}' scheme, sent in {where}.",
                };
            }

            case SecuritySchemeType.Http when string.Equals(scheme.Scheme, "basic", StringComparison.OrdinalIgnoreCase):
                return new SecuritySchemeModel
                {
                    Key = key,
                    Kind = SecurityKind.HttpBasic,
                    PrimaryProperty = property(key + "Username"),
                    SecondaryProperty = property(key + "Password"),
                    Description = $"HTTP Basic credentials for the '{key}' scheme.",
                };

            case SecuritySchemeType.Http when string.Equals(scheme.Scheme, "bearer", StringComparison.OrdinalIgnoreCase):
                return new SecuritySchemeModel
                {
                    Key = key,
                    Kind = SecurityKind.HttpBearer,
                    PrimaryProperty = property(key),
                    Description = $"Bearer token for the '{key}' scheme.",
                };

            case SecuritySchemeType.OAuth2:
                return new SecuritySchemeModel
                {
                    Key = key,
                    Kind = SecurityKind.OAuth2,
                    PrimaryProperty = property(key),
                    Description = $"OAuth 2.0 access token for the '{key}' scheme.",
                };

            case SecuritySchemeType.OpenIdConnect:
                return new SecuritySchemeModel
                {
                    Key = key,
                    Kind = SecurityKind.OpenIdConnect,
                    PrimaryProperty = property(key),
                    Description = $"OpenID Connect token for the '{key}' scheme.",
                };

            default:
                return null; // http digest, mutualTLS, etc.
        }
    }

    // ---- helpers --------------------------------------------------------

    private static readonly string[] NoSuffix = [""];

    /// <summary>
    /// Assigns each resource a unique, meaningful name. A leaf segment that is unique keeps its
    /// short name (e.g. "Pet"); colliding leaves are disambiguated with parent path context
    /// (e.g. "AccountUser" / "ZoneUser") rather than numeric suffixes. Variant groups are treated as
    /// a single naming unit — the parent base is qualified by path and the rename cascades to every
    /// variant — so two same-named groups become "AccountRule"/"ZoneRule", never "Rule"/"Rule2".
    /// Names are also kept clear of reserved infrastructure/BCL and generated type names.
    /// </summary>
    private static void AssignNames(List<ResourceModel> resources, ExtensionModel model)
    {
        // Names that must not be used directly: the extension's own types, BCL types in scope,
        // and every generated enum/nested type (a resource named the same as a nested type would
        // make handler references ambiguous and clash with the resource's own sub-namespace).
        var claimed = new HashSet<string>(StringComparer.Ordinal) { "Configuration", "Program", "ResourceHandlerBase" };
        claimed.UnionWith(NameUtil.ReservedTypeNames);
        claimed.UnionWith(model.NestedTypes.Select(t => t.Name));
        claimed.UnionWith(model.Enums.Select(e => e.Name));

        // One naming unit per standalone resource and per variant group (keyed by Group). Recorded in
        // first-sighting order for deterministic, reproducible names.
        var groupIndices = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var units = new List<(bool IsGroup, string Key, int Index, IReadOnlyList<string> Segments, string Leaf)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < resources.Count; i++)
        {
            var r = resources[i];
            var leaf = r.NameSegments.Count > 0 ? r.NameSegments[^1] : r.Name;
            if (r.Group is null)
            {
                units.Add((false, "", i, r.NameSegments, leaf));
            }
            else
            {
                if (!groupIndices.TryGetValue(r.Group, out var list))
                    groupIndices[r.Group] = list = [];
                list.Add(i);
                if (seen.Add(r.Group))
                    units.Add((true, r.Group, i, r.NameSegments, leaf));
            }
        }

        // A leaf shared by several units is ambiguous; all such units qualify to the same minimal
        // depth at which they become mutually distinct, so the result is symmetric.
        var peersByLeaf = units.GroupBy(u => u.Leaf).ToDictionary(g => g.Key, g => g.Select(u => u.Segments).ToList(), StringComparer.Ordinal);

        foreach (var unit in units)
        {
            var peers = peersByLeaf[unit.Leaf];
            var startDepth = peers.Count > 1 ? MinimalDistinctDepth(peers, unit.Leaf) : 1;

            if (unit.IsGroup)
            {
                var indices = groupIndices[unit.Key];
                var suffixes = indices.Select(idx => SuffixOf(resources[idx].Name, unit.Key)).ToList();
                var name = ResolveName(unit.Segments, unit.Leaf, startDepth, suffixes, claimed, model);
                foreach (var sfx in suffixes)
                    claimed.Add(name + sfx);
                if (!string.Equals(name, unit.Key, StringComparison.Ordinal))
                    RenameGroup(resources, indices, unit.Key, name);
            }
            else
            {
                var name = ResolveName(unit.Segments, unit.Leaf, startDepth, NoSuffix, claimed, model);
                claimed.Add(name);
                var r = resources[unit.Index];
                if (!string.Equals(name, r.Name, StringComparison.Ordinal))
                    resources[unit.Index] = CloneWithNames(r, name, group: null, baseType: null, identifiersType: null);
            }
        }
    }

    /// <summary>The smallest depth at which qualifying every peer's path yields mutually distinct names.</summary>
    private static int MinimalDistinctDepth(List<IReadOnlyList<string>> peers, string leaf)
    {
        var maxDepth = peers.Max(s => Math.Max(1, s.Count));
        for (var depth = 2; depth <= maxDepth; depth++)
        {
            var names = peers.Select(s => Qualify(s, leaf, depth)).ToList();
            if (names.Distinct(StringComparer.Ordinal).Count() == names.Count)
                return depth;
        }

        return maxDepth; // identical paths can't be distinguished — ResolveName falls back to a suffix
    }

    /// <summary>The portion of a group member's name after its (current) group/parent prefix.</summary>
    private static string SuffixOf(string memberName, string parentName) =>
        memberName.StartsWith(parentName, StringComparison.Ordinal) ? memberName[parentName.Length..] : memberName;

    /// <summary>Renames a whole variant group: the parent base, its identifiers class, and every variant.</summary>
    private static void RenameGroup(List<ResourceModel> resources, List<int> indices, string oldName, string newName)
    {
        var oldIdentifiers = oldName + "Identifiers";
        var newIdentifiers = newName + "Identifiers";

        foreach (var idx in indices)
        {
            var r = resources[idx];
            var name = newName + SuffixOf(r.Name, oldName);
            var baseType = r.BaseTypeName == oldIdentifiers ? newIdentifiers   // the parent base
                : r.BaseTypeName == oldName ? newName                          // a variant
                : r.BaseTypeName;
            var identifiers = r.IdentifiersTypeName == oldIdentifiers ? newIdentifiers : r.IdentifiersTypeName;
            resources[idx] = CloneWithNames(r, name, group: newName, baseType, identifiers);
        }
    }

    /// <summary>
    /// Picks the shortest parent-qualified name (starting at <paramref name="startDepth"/>) such that
    /// the name and all required suffixes are free, growing context as needed and only falling back to
    /// a numeric suffix when there is no path context left to disambiguate.
    /// </summary>
    private static string ResolveName(IReadOnlyList<string> segments, string leaf, int startDepth, IReadOnlyList<string> requiredSuffixes, HashSet<string> claimed, ExtensionModel model)
    {
        bool Free(string candidate) => requiredSuffixes.All(sfx => !claimed.Contains(candidate + sfx));

        var maxDepth = Math.Max(1, segments.Count);
        var depth = Math.Clamp(startDepth, 1, maxDepth);

        string name;
        do
        {
            name = Qualify(segments, leaf, depth);
            if (Free(name))
                return name;
            depth++;
        }
        while (depth <= maxDepth);

        // No context left (e.g. a top-level resource colliding with a reserved name): suffix.
        var baseName = name;
        var suffix = 2;
        while (!Free($"{baseName}{suffix}"))
            suffix++;
        var unique = $"{baseName}{suffix}";
        model.Warnings.Add($"Resource name '{leaf}' could not be disambiguated by path; using '{unique}'.");
        return unique;
    }

    /// <summary>
    /// Concatenates the last <paramref name="depth"/> path segments into a PascalCase name,
    /// collapsing a redundant trailing repeat (e.g. "AccessRule" + "Rule" -> "AccessRule").
    /// </summary>
    private static string Qualify(IReadOnlyList<string> segments, string leaf, int depth)
    {
        if (segments.Count == 0)
            return leaf;

        var take = Math.Min(depth, segments.Count);
        var name = "";
        foreach (var segment in segments.Skip(segments.Count - take))
        {
            if (name.Length > 0 && name.EndsWith(segment, StringComparison.Ordinal))
                continue;
            name += segment;
        }

        return name;
    }

    /// <summary>Clones a resource with new name/grouping fields, preserving everything else (incl. properties).</summary>
    private static ResourceModel CloneWithNames(ResourceModel old, string name, string? group, string? baseType, string? identifiersType)
    {
        var clone = new ResourceModel
        {
            Name = name,
            ResourceTypeName = name,
            NameSegments = old.NameSegments,
            TypePath = old.TypePath,
            Category = old.Category,
            Summary = old.Summary,
            Description = old.Description,
            Ops = old.Ops,
            Group = group,
            BaseTypeName = baseType,
            IdentifiersTypeName = identifiersType,
            OwnsIdentifiers = old.OwnsIdentifiers,
            EmitResourceType = old.EmitResourceType,
            IsAbstract = old.IsAbstract,
        };
        clone.Properties.AddRange(old.Properties);
        return clone;
    }

    /// <summary>
    /// Guarantees every deployable resource's child-path type is unique. Distinct paths are the norm
    /// (the path hierarchy disambiguates); only genuinely identical paths (e.g. two same-shaped action
    /// endpoints) need a numeric suffix on the leaf segment.
    /// </summary>
    private static void EnsureUniqueTypePaths(List<ResourceModel> resources)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in resources)
        {
            if (!r.EmitResourceType || r.TypePath.Count == 0)
                continue;

            if (seen.Add(string.Join("/", r.TypePath)))
                continue;

            var path = r.TypePath.ToList();
            var leaf = path[^1];
            var suffix = 2;
            while (!seen.Add(string.Join("/", path[..^1].Append($"{leaf}{suffix}"))))
                suffix++;
            path[^1] = $"{leaf}{suffix}";
            r.TypePath = path;
        }
    }

    /// <summary>Each path parameter paired with the container segment immediately preceding it.</summary>
    private static List<(string Param, string? Container)> ParamsWithContainers(string path)
    {
        var result = new List<(string, string?)>();
        string? lastContainer = null;
        foreach (var segment in path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = PathParam.Match(segment);
            if (match.Success)
                result.Add((match.Groups[1].Value, lastContainer));
            else
                lastContainer = segment;
        }

        return result;
    }

    /// <summary>The document's paths with anything matching the exclusion filter removed.</summary>
    private IDictionary<string, IOpenApiPathItem> IncludedPaths()
    {
        var result = new Dictionary<string, IOpenApiPathItem>(StringComparer.Ordinal);
        if (_doc.Paths is not { } paths)
            return result;

        foreach (var (key, item) in paths)
            if (!IsExcludedPath(key, item))
                result[key] = item;

        return result;
    }

    /// <summary>Whether a path is excluded by template glob or because one of its operations carries an excluded tag.</summary>
    private bool IsExcludedPath(string path, IOpenApiPathItem item)
    {
        if (_filter.IsEmpty)
            return false;
        if (_filter.MatchesName(path))
            return true;

        if (item.Operations is { } ops)
            foreach (var op in ops.Values)
                if (op.Tags is { } tags)
                    foreach (var tag in tags)
                        if (tag.Reference?.Id is { } id && _filter.MatchesTag(id))
                            return true;

        return false;
    }

    private static List<string> SegmentsOf(string path) =>
        path.Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => !PathParam.IsMatch(s))
            .Select(NameUtil.SingularPascal)
            .ToList();

    private static OpenApiOperation? GetOp(IOpenApiPathItem? def, HttpMethod method) =>
        def?.Operations is { } ops && ops.TryGetValue(method, out var op) ? op : null;

    private static IOpenApiSchema? RequestSchema(OpenApiOperation? op) =>
        op?.RequestBody?.Content is { } content ? JsonSchema(content) : null;

    private static IOpenApiSchema? ResponseSchema(OpenApiOperation? op)
    {
        if (op?.Responses is null)
            return null;

        foreach (var code in new[] { "200", "201", "202" })
            if (op.Responses.TryGetValue(code, out var r) && r.Content is { } c && JsonSchema(c) is { } s)
                return s;

        if (op.Responses.TryGetValue("default", out var def) && def.Content is { } dc)
            return JsonSchema(dc);

        return null;
    }

    private static IOpenApiSchema? CollectionItemSchema(OpenApiOperation? op)
    {
        var schema = ResponseSchema(op);
        if (schema is null)
            return null;
        return (schema.Type & ~JsonSchemaType.Null) == JsonSchemaType.Array ? schema.Items : schema;
    }

    private static IOpenApiSchema? JsonSchema(IDictionary<string, IOpenApiMediaType> content)
    {
        if (content.TryGetValue("application/json", out var json))
            return json.Schema;
        return content.Values.FirstOrDefault()?.Schema;
    }

    private static bool EndsWithParam(string path) => PathParam.IsMatch(LastSegment(path));

    private static string StripTrailingParam(string path)
    {
        var slash = path.TrimEnd('/').LastIndexOf('/');
        return slash <= 0 ? "/" : path[..slash];
    }

    private static string LastSegment(string path)
    {
        var trimmed = path.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash < 0 ? trimmed : trimmed[(slash + 1)..];
    }

    private static string? LastNonParamSegment(string path)
    {
        var segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = segments.Length - 1; i >= 0; i--)
            if (!PathParam.IsMatch(segments[i]))
                return segments[i];
        return null;
    }

    private static string Relative(string path) => path.TrimStart('/');

    [GeneratedRegex(@"\{([^}]+)\}")]
    private static partial Regex ParamRegex();
}
