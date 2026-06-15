using Tankerkiller125.BicepExtensionGen.OpenApi;

namespace Tankerkiller125.BicepExtensionGen.Cli;

/// <summary>Parsed command-line options for the generate command.</summary>
public sealed class CliOptions
{
    public required string Input { get; init; }
    public required string Output { get; init; }
    public required string Name { get; init; }
    public string Version { get; init; } = "0.1.0";
    public string? Namespace { get; init; }
    public MappingMode Mapping { get; init; } = MappingMode.Path;
    public bool Docs { get; init; }
    public bool Force { get; init; }

    /// <summary>Optional namespace prefix for Bicep resource types, e.g. <c>Cloudflare.Dns</c>.</summary>
    public string? ResourceTypePrefix { get; init; }

    /// <summary>Use hierarchical (Azure-style child-path) resource types, e.g. <c>zones/dnsRecords/A</c>.</summary>
    public bool ResourceTypePath { get; init; }

    /// <summary>Globs matched against path templates (path mode) or schema names (schema mode) to exclude.</summary>
    public IReadOnlyList<string> ExcludePaths { get; init; } = [];

    /// <summary>Globs matched against operation tags to exclude.</summary>
    public IReadOnlyList<string> ExcludeTags { get; init; } = [];

    public static CliOptions Parse(string[] args)
    {
        string? input = null, output = null, name = null, ns = null;
        var version = "0.1.0";
        var mapping = MappingMode.Path;
        bool docs = false, force = false;
        string? resourcePrefix = null;
        var resourcePath = false;
        var excludePaths = new List<string>();
        var excludeTags = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "generate":
                    break; // the only command; accepted and ignored
                case "-i" or "--input":
                    input = Next(args, ref i, arg);
                    break;
                case "-o" or "--output":
                    output = Next(args, ref i, arg);
                    break;
                case "-n" or "--name":
                    name = Next(args, ref i, arg);
                    break;
                case "--version":
                    version = Next(args, ref i, arg);
                    break;
                case "--namespace":
                    ns = Next(args, ref i, arg);
                    break;
                case "--mapping":
                    mapping = ParseMapping(Next(args, ref i, arg));
                    break;
                case "--resource-prefix":
                    resourcePrefix = Next(args, ref i, arg);
                    break;
                case "--resource-path":
                    resourcePath = true;
                    break;
                case "--exclude":
                    excludePaths.AddRange(SplitList(Next(args, ref i, arg)));
                    break;
                case "--exclude-tag":
                    excludeTags.AddRange(SplitList(Next(args, ref i, arg)));
                    break;
                case "--docs":
                    docs = true;
                    break;
                case "--force":
                    force = true;
                    break;
                default:
                    throw new ArgumentException($"unknown argument '{arg}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("--input is required.");
        if (string.IsNullOrWhiteSpace(output))
            throw new ArgumentException("--output is required.");
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("--name is required.");

        return new CliOptions
        {
            Input = input,
            Output = Path.GetFullPath(output),
            Name = name,
            Version = version,
            Namespace = ns,
            Mapping = mapping,
            Docs = docs,
            Force = force,
            ResourceTypePrefix = resourcePrefix,
            ResourceTypePath = resourcePath,
            ExcludePaths = excludePaths,
            ExcludeTags = excludeTags,
        };
    }

    private static string Next(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"missing value for '{flag}'.");
        return args[++i];
    }

    /// <summary>Splits a comma-separated option value into trimmed, non-empty entries.</summary>
    private static IEnumerable<string> SplitList(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static MappingMode ParseMapping(string value) => value.ToLowerInvariant() switch
    {
        "path" => MappingMode.Path,
        "schema" => MappingMode.Schema,
        _ => throw new ArgumentException($"invalid --mapping '{value}' (expected: path, schema)."),
    };
}
