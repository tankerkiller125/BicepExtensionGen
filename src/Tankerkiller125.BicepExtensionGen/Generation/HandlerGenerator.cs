using System.Text.RegularExpressions;
using Tankerkiller125.BicepExtensionGen.Model;

namespace Tankerkiller125.BicepExtensionGen.Generation;

/// <summary>Emits the REST handler base class and one wired handler per resource.</summary>
public static partial class HandlerGenerator
{
    /// <summary>Generates <c>Handlers/&lt;Resource&gt;Handler.cs</c> with CRUD wired to the mapped endpoints.</summary>
    public static string HandlerFile(ExtensionModel ext, ResourceModel r, string rootNs)
    {
        var t = r.Name;
        var firstId = r.Identifiers.First().Name;
        var w = new CodeWriter();

        w.Line("using System.Net.Http;");
        w.Line("using Microsoft.Extensions.Logging;");
        w.Line($"using {rootNs}.Models;");
        w.Line($"using {rootNs}.Models.Common;");
        w.Line($"using {rootNs}.Models.{r.ModelNamespaceSegment};");
        w.Blank();
        w.Line($"namespace {rootNs}.Handlers;");
        w.Blank();
        w.Line($"/// <summary>Handler for {t} resources.</summary>");

        using (w.Block($"public class {t}Handler : ResourceHandlerBase<{t}, {r.EffectiveIdentifiersType}>"))
        {
            w.Line($"public {t}Handler(ILogger<{t}Handler> logger) : base(logger) {{ }}");
            w.Blank();

            WritePreview(w, r);
            WriteCreateOrUpdate(w, r, t, firstId);
            WriteGetIdentifiers(w, r, t);
            WriteGet(w, r);
            WriteCreate(w, r);
            WriteUpdate(w, r);
            WriteDelete(w, r);
        }

        return w.ToString();
    }

    private static void WritePreview(CodeWriter w, ResourceModel r)
    {
        using (w.Block("protected override async Task<ResourceResponse> Preview(ResourceRequest request, CancellationToken cancellationToken)"))
        {
            w.Line("var existing = await GetResourceAsync(request.Config, request.Properties, cancellationToken);");
            using (w.Block("if (existing is not null)"))
                WriteOutputAssignments(w, r, target: "request.Properties", source: "existing");
            w.Line("return GetResponse(request);");
        }
        w.Blank();
    }

    private static void WriteCreateOrUpdate(CodeWriter w, ResourceModel r, string t, string firstId)
    {
        using (w.Block("protected override async Task<ResourceResponse> CreateOrUpdate(ResourceRequest request, CancellationToken cancellationToken)"))
        {
            w.Line("var props = request.Properties;");
            w.Line($"_logger.LogInformation(\"Ensuring {t} {{Identifier}}\", props.{firstId});");
            w.Blank();
            w.Line("var existing = await GetResourceAsync(request.Config, props, cancellationToken);");

            var hasCreate = r.Ops.Create is not null;
            var hasUpdate = r.Ops.Update is not null;
            if (hasCreate && hasUpdate)
            {
                using (w.Block("if (existing is null)"))
                    w.Line("await CreateResourceAsync(request.Config, props, cancellationToken);");
                using (w.Block("else"))
                    w.Line("await UpdateResourceAsync(request.Config, props, cancellationToken);");
            }
            else if (hasCreate)
            {
                using (w.Block("if (existing is null)"))
                    w.Line("await CreateResourceAsync(request.Config, props, cancellationToken);");
                using (w.Block("else"))
                    w.Line($"_logger.LogWarning(\"No update endpoint for {t}; leaving the existing resource unchanged.\");");
            }
            else if (hasUpdate)
            {
                w.Line("_ = existing; // update endpoint behaves as an upsert");
                w.Line("await UpdateResourceAsync(request.Config, props, cancellationToken);");
            }
            else
            {
                w.Line("_ = existing;");
                w.Line($"_logger.LogWarning(\"No create or update endpoint is defined for {t}.\");");
            }

            w.Blank();
            w.Line("var latest = await GetResourceAsync(request.Config, props, cancellationToken);");
            using (w.Block("if (latest is not null)"))
                WriteOutputAssignments(w, r, target: "props", source: "latest");
            w.Line("return GetResponse(request);");
        }
        w.Blank();
    }

    private static void WriteGetIdentifiers(CodeWriter w, ResourceModel r, string t)
    {
        w.Line($"protected override {r.EffectiveIdentifiersType} GetIdentifiers({t} properties) => new()");
        using (w.Brace("};"))
        {
            foreach (var id in r.Identifiers)
                w.Line($"{id.Name} = properties.{id.Name},");
        }
        w.Blank();
    }

    private static void WriteOutputAssignments(CodeWriter w, ResourceModel r, string target, string source)
    {
        var outputs = r.Outputs.ToList();
        if (outputs.Count == 0)
        {
            w.Line("// No read-only output properties to populate.");
            return;
        }

        foreach (var o in outputs)
            w.Line($"{target}.{o.Name} = {source}.{o.Name};");
    }

    private static void WriteGet(CodeWriter w, ResourceModel r)
    {
        using (w.Block($"private async Task<{r.Name}?> GetResourceAsync(Configuration configuration, {r.Name} props, CancellationToken ct)"))
        {
            if (r.Ops.Read is null)
            {
                w.Line("// No read endpoint is defined for this resource.");
                w.Line("await Task.CompletedTask;");
                w.Line("return null;");
            }
            else
            {
                using (w.Block("try"))
                    w.Line($"return await CallApiForResponse<{r.Name}>(configuration, HttpMethod.Get, $\"{BuildPath(r, r.Ops.Read.PathTemplate)}\", ct);");
                using (w.Block("catch"))
                    w.Line("return null; // Treat a failed lookup as \"not found\".");
            }
        }
        w.Blank();
    }

    private static void WriteCreate(CodeWriter w, ResourceModel r)
    {
        if (r.Ops.Create is null)
            return;

        using (w.Block($"private async Task CreateResourceAsync(Configuration configuration, {r.Name} props, CancellationToken ct)"))
            w.Line($"await CallApiForResponse<{r.Name}>(configuration, HttpMethod.Post, $\"{BuildPath(r, r.Ops.Create.PathTemplate)}\", ct, props);");
        w.Blank();
    }

    private static void WriteUpdate(CodeWriter w, ResourceModel r)
    {
        if (r.Ops.Update is null)
            return;

        using (w.Block($"private async Task UpdateResourceAsync(Configuration configuration, {r.Name} props, CancellationToken ct)"))
            w.Line($"await CallApiForResponse<{r.Name}>(configuration, HttpMethod.{r.Ops.Update.Method}, $\"{BuildPath(r, r.Ops.Update.PathTemplate)}\", ct, props);");
        w.Blank();
    }

    private static void WriteDelete(CodeWriter w, ResourceModel r)
    {
        if (r.Ops.Delete is null)
            return;

        w.Line("/// <summary>Deletes the resource via the mapped DELETE endpoint.</summary>");
        using (w.Block($"public async Task DeleteResourceAsync(Configuration configuration, {r.Name} props, CancellationToken ct)"))
            w.Line($"await CallApi(configuration, HttpMethod.Delete, $\"{BuildPath(r, r.Ops.Delete.PathTemplate)}\", ct);");
        w.Blank();
    }

    /// <summary>Turns a path template into the body of a C# interpolated string, substituting identifiers.</summary>
    private static string BuildPath(ResourceModel r, string template)
    {
        // First identifier wins if a raw param name is (unusually) repeated in the path.
        var byParam = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var i in r.Identifiers.Where(i => i.PathParamName is not null))
            byParam.TryAdd(i.PathParamName!, i.Name);

        return ParamRegex().Replace(template, m =>
        {
            var param = m.Groups[1].Value;
            return byParam.TryGetValue(param, out var prop)
                ? $"{{Uri.EscapeDataString(props.{prop}.ToString()!)}}"
                : $"{{{{{param}}}}}"; // unmatched: emit a literal brace pair
        });
    }

    /// <summary>The REST base class, applying every credential the user has configured.</summary>
    public static string BaseClass(ExtensionModel ext, string rootNs)
    {
        var w = new CodeWriter();

        w.Line("using System.Net.Http;");
        w.Line("using System.Net.Http.Headers;");
        w.Line("using System.Text;");
        w.Line("using System.Text.Json;");
        w.Line("using Bicep.Local.Extension.Host.Handlers;");
        w.Line("using Microsoft.Extensions.Logging;");
        w.Line($"using {rootNs}.Models;");
        w.Blank();
        w.Line($"namespace {rootNs}.Handlers;");
        w.Blank();
        w.Line("/// <summary>");
        w.Line("/// Base class for all resource handlers. Provides REST helpers over the configured base URL.");
        w.Line("/// Applies whichever credentials are present in <see cref=\"Configuration\"/>.");
        w.Line("/// </summary>");
        w.Line("public abstract class ResourceHandlerBase<TProps, TIdentifiers>");
        w.Indent();
        w.Line(": TypedResourceHandler<TProps, TIdentifiers, Configuration>");
        w.Line("where TProps : class");
        w.Line("where TIdentifiers : class");
        w.Outdent();

        using (w.Brace())
        {
            w.Lines(Fields);
            w.Blank();

            using (w.Block("protected static HttpClient CreateClient(Configuration configuration)"))
            {
                w.Line("var client = new HttpClient { BaseAddress = new Uri(configuration.BaseUrl.TrimEnd('/')) };");
                w.Line("client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(\"application/json\"));");
                w.Blank();
                WriteAuthApplication(w, ext);
                w.Blank();
                w.Line("return client;");
            }
            w.Blank();

            w.Line("/// <summary>Appends any configured apiKey-in-query credentials to a request URI.</summary>");
            using (w.Block("protected static string ApplyQueryAuth(Configuration configuration, string uri)"))
            {
                w.Line("var pairs = new List<string>();");
                WriteQueryAuth(w, ext);
                using (w.Block("if (pairs.Count == 0)"))
                    w.Line("return uri;");
                w.Line("return uri + (uri.Contains('?') ? \"&\" : \"?\") + string.Join(\"&\", pairs);");
            }
            w.Blank();

            w.Lines(CallApiMethods);
        }

        return w.ToString();
    }

    /// <summary>Emits the per-scheme credential application inside <c>CreateClient</c>.</summary>
    private static void WriteAuthApplication(CodeWriter w, ExtensionModel ext)
    {
        var schemes = ext.SecuritySchemes.Where(s => s.Kind != SecurityKind.ApiKeyQuery).ToList();
        if (schemes.Count == 0)
        {
            w.Line("// No authentication scheme was detected in the OpenAPI document.");
            return;
        }

        foreach (var s in schemes)
        {
            switch (s.Kind)
            {
                case SecurityKind.ApiKeyHeader:
                    WriteGuarded(w, s.PrimaryProperty,
                        $"client.DefaultRequestHeaders.TryAddWithoutValidation(\"{s.ParameterName}\", configuration.{s.PrimaryProperty});");
                    break;
                case SecurityKind.ApiKeyCookie:
                    WriteGuarded(w, s.PrimaryProperty,
                        $"client.DefaultRequestHeaders.TryAddWithoutValidation(\"Cookie\", $\"{s.ParameterName}={{configuration.{s.PrimaryProperty}}}\");");
                    break;
                case SecurityKind.HttpBasic:
                    w.Line($"if (!string.IsNullOrWhiteSpace(configuration.{s.PrimaryProperty}) && !string.IsNullOrWhiteSpace(configuration.{s.SecondaryProperty}))");
                    w.Indent();
                    w.Line("client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(\"Basic\",");
                    w.Line($"    Convert.ToBase64String(Encoding.UTF8.GetBytes($\"{{configuration.{s.PrimaryProperty}}}:{{configuration.{s.SecondaryProperty}}}\")));");
                    w.Outdent();
                    break;
                case SecurityKind.HttpBearer:
                case SecurityKind.OAuth2:
                case SecurityKind.OpenIdConnect:
                    WriteGuarded(w, s.PrimaryProperty,
                        $"client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(\"Bearer\", configuration.{s.PrimaryProperty});");
                    break;
            }
        }
    }

    /// <summary>Emits the apiKey-in-query credential application inside <c>ApplyQueryAuth</c>.</summary>
    private static void WriteQueryAuth(CodeWriter w, ExtensionModel ext)
    {
        var schemes = ext.SecuritySchemes.Where(s => s.Kind == SecurityKind.ApiKeyQuery).ToList();
        if (schemes.Count == 0)
        {
            w.Line("// No query-parameter credentials.");
            return;
        }

        foreach (var s in schemes)
            WriteGuarded(w, s.PrimaryProperty,
                $"pairs.Add($\"{s.ParameterName}={{Uri.EscapeDataString(configuration.{s.PrimaryProperty})}}\");");
    }

    /// <summary>Writes <c>if (credential set) statement;</c> with the body indented.</summary>
    private static void WriteGuarded(CodeWriter w, string property, string statement)
    {
        w.Line($"if (!string.IsNullOrWhiteSpace(configuration.{property}))");
        w.Indent();
        w.Line(statement);
        w.Outdent();
    }

    private const string Fields =
        """
        protected readonly ILogger _logger;

        protected ResourceHandlerBase(ILogger logger) => _logger = logger;

        protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = false,
        };
        """;

    private const string CallApiMethods =
        """
        protected async Task<T?> CallApiForResponse<T>(
            Configuration configuration,
            HttpMethod method,
            string relativePath,
            CancellationToken ct,
            object? payload = null)
        {
            using var client = CreateClient(configuration);
            var requestUri = ApplyQueryAuth(configuration, $"{configuration.BaseUrl.TrimEnd('/')}/{relativePath.TrimStart('/')}");
            _logger.LogInformation("{Method} {Uri}", method, requestUri);

            using var message = new HttpRequestMessage(method, requestUri);
            if (payload is not null)
            {
                var json = JsonSerializer.Serialize(payload, JsonOptions);
                message.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            var response = await client.SendAsync(message, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("API call failed: {StatusCode} {Reason} Body={Body}", (int)response.StatusCode, response.ReasonPhrase, body);
                throw new InvalidOperationException($"API call failed: {(int)response.StatusCode} {response.ReasonPhrase} Body={body}");
            }

            if (typeof(T) == typeof(object) || response.Content.Headers.ContentLength == 0)
            {
                return default;
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            return string.IsNullOrWhiteSpace(responseBody)
                ? default
                : JsonSerializer.Deserialize<T>(responseBody, JsonOptions);
        }

        protected async Task CallApi(
            Configuration configuration,
            HttpMethod method,
            string relativePath,
            CancellationToken ct,
            object? payload = null)
        {
            await CallApiForResponse<object>(configuration, method, relativePath, ct, payload);
        }
        """;

    [GeneratedRegex(@"\{([^}]+)\}")]
    private static partial Regex ParamRegex();
}
