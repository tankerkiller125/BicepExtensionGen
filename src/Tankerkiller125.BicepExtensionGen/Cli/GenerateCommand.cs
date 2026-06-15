using Tankerkiller125.BicepExtensionGen.Generation;
using Tankerkiller125.BicepExtensionGen.OpenApi;

namespace Tankerkiller125.BicepExtensionGen.Cli;

/// <summary>Parses CLI arguments and orchestrates the OpenAPI → Bicep extension generation.</summary>
public static class GenerateCommand
{
    private const int ExitOk = 0;
    private const int ExitError = 1;
    private const int ExitWarnings = 2;

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return args.Length == 0 ? ExitError : ExitOk;
        }

        CliOptions options;
        try
        {
            options = CliOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            Console.Error.WriteLine("Run with --help for usage.");
            return ExitError;
        }

        try
        {
            return await ExecuteAsync(options);
        }
        catch (OpenApiLoadException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return ExitError;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: unexpected failure: {ex.Message}");
            return ExitError;
        }
    }

    private static async Task<int> ExecuteAsync(CliOptions options)
    {
        // Guard the output directory.
        if (Directory.Exists(options.Output) && Directory.EnumerateFileSystemEntries(options.Output).Any() && !options.Force)
        {
            Console.Error.WriteLine($"error: output directory '{options.Output}' is not empty. Use --force to overwrite.");
            return ExitError;
        }

        Console.WriteLine($"Loading OpenAPI document: {options.Input}");
        var load = await OpenApiLoader.LoadAsync(options.Input);
        foreach (var warning in load.Warnings)
            Console.WriteLine($"  openapi warning: {warning}");

        var rootNs = NameUtil.Namespace(options.Namespace ?? options.Name);
        var filter = new ExclusionFilter(options.ExcludePaths, options.ExcludeTags);
        if (!filter.IsEmpty)
        {
            if (options.ExcludePaths.Count > 0)
                Console.WriteLine($"Excluding paths/schemas matching: {string.Join(", ", options.ExcludePaths)}");
            if (options.ExcludeTags.Count > 0)
                Console.WriteLine($"Excluding tags matching: {string.Join(", ", options.ExcludeTags)}");
        }

        if (!string.IsNullOrWhiteSpace(options.ResourceTypePrefix))
            Console.WriteLine($"Prefixing Bicep resource types with: {options.ResourceTypePrefix}/");
        if (options.ResourceTypePath)
            Console.WriteLine("Using hierarchical (child-path) Bicep resource types.");

        var builder = new ResourceModelBuilder(load.Document, options.Name, options.Version, options.Mapping, filter, options.ResourceTypePrefix, options.ResourceTypePath);
        var model = builder.Build();

        var deployable = model.Resources.Where(r => r.EmitResourceType).ToList();
        Console.WriteLine($"Discovered {deployable.Count} resource(s): {string.Join(", ", deployable.Select(model.QualifiedResourceType))}");

        var writer = new ExtensionWriter(options.Output, rootNs);
        var files = writer.Write(model);
        Console.WriteLine($"Wrote {files.Count} file(s) to {options.Output}");

        if (options.Docs)
        {
            Console.WriteLine("Generating documentation with bicep-local-docgen...");
            var docs = await DocsRunner.GenerateAsync(options.Output, model);
            if (docs.Success)
                Console.WriteLine("Documentation generated under docs/.");
            else
            {
                Console.Error.WriteLine("warning: documentation generation failed:");
                Console.Error.WriteLine(docs.Output);
                model.Warnings.Add("Documentation generation failed; see output above.");
            }
        }

        foreach (var warning in model.Warnings)
            Console.WriteLine($"  warning: {warning}");

        return model.Warnings.Count > 0 ? ExitWarnings : ExitOk;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            bicepextgen — generate a .NET Bicep extension from an OpenAPI 3.0 document.

            Usage:
              bicepextgen generate --input <file|url> --output <dir> --name <Ext.Name> [options]

            Required:
              -i, --input <path>      OpenAPI 3.0 document (JSON or YAML), local path or http(s) URL.
              -o, --output <dir>      Directory to write the generated extension into.
              -n, --name <name>       Extension name, e.g. "Contoso.Api" (also the root namespace).

            Options:
                  --version <ver>     Extension version (default: 0.1.0).
                  --namespace <ns>    C# root namespace (default: derived from --name).
                  --mapping <mode>    Resource mapping: path (default) or schema.
                  --resource-prefix <p>  Namespace Bicep resource types, e.g. "Cloudflare.Dns" yields
                                      type names like "Cloudflare.Dns/DnsRecordA".
                  --resource-path     Use hierarchical child-path types from the API path, e.g.
                                      "Zone/DnsRecord/A" (composes with --resource-prefix).
                  --exclude <glob>    Drop paths (path mode) or schemas (schema mode) matching a glob.
                                      Comma-separated and/or repeatable. '*' = within a segment,
                                      '**' = across segments. e.g. --exclude "**/ai/run/**"
                  --exclude-tag <g>   Drop operations whose tag matches a glob. Comma-separated and/or
                                      repeatable. e.g. --exclude-tag "Workers AI*"
                  --docs              Run bicep-local-docgen to produce markdown docs.
                  --force             Overwrite a non-empty output directory.
              -h, --help              Show this help.

            Exit codes: 0 success, 1 error, 2 success with warnings.
            """);
    }
}
