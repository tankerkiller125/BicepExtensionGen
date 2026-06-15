using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

namespace Tankerkiller125.BicepExtensionGen.OpenApi;

/// <summary>Loads and parses an OpenAPI 3.x document from a file path or URL.</summary>
public static class OpenApiLoader
{
    /// <summary>Result of loading an OpenAPI document.</summary>
    public sealed record LoadResult(OpenApiDocument Document, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

    /// <summary>
    /// Reads the document at <paramref name="input"/> (a local file path or http(s) URL).
    /// Supports JSON and YAML; the format is inferred from the extension/content.
    /// Throws <see cref="OpenApiLoadException"/> when no usable document can be produced.
    /// </summary>
    public static async Task<LoadResult> LoadAsync(string input, CancellationToken ct = default)
    {
        var settings = new OpenApiReaderSettings();
        settings.AddYamlReader();

        ReadResult result;
        try
        {
            if (Uri.TryCreate(input, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
            {
                result = await OpenApiDocument.LoadAsync(input, settings, ct).ConfigureAwait(false);
            }
            else
            {
                if (!File.Exists(input))
                    throw new OpenApiLoadException($"Input file not found: {input}");

                var format = InferFormat(input);
                await using var stream = File.OpenRead(input);
                result = await OpenApiDocument.LoadAsync(stream, format, settings, ct).ConfigureAwait(false);
            }
        }
        catch (OpenApiLoadException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new OpenApiLoadException($"Failed to read OpenAPI document '{input}': {ex.Message}", ex);
        }

        var errors = result.Diagnostic?.Errors.Select(e => e.ToString()).ToList() ?? [];
        var warnings = result.Diagnostic?.Warnings.Select(w => w.ToString()).ToList() ?? [];

        if (result.Document is null)
            throw new OpenApiLoadException($"OpenAPI document '{input}' could not be parsed:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");

        return new LoadResult(result.Document, errors, warnings);
    }

    private static string InferFormat(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".yaml" or ".yml" ? "yaml" : "json";
    }
}

/// <summary>Raised when an OpenAPI document cannot be loaded or parsed into a usable model.</summary>
public sealed class OpenApiLoadException : Exception
{
    public OpenApiLoadException(string message, Exception? inner = null) : base(message, inner) { }
}
