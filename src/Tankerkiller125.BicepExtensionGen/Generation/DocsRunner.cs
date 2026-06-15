using System.Diagnostics;
using Tankerkiller125.BicepExtensionGen.Model;

namespace Tankerkiller125.BicepExtensionGen.Generation;

/// <summary>Invokes the <c>bicep-local-docgen</c> tool to produce markdown docs for a generated extension.</summary>
public static class DocsRunner
{
    public sealed record DocsResult(bool Success, string Output);

    /// <summary>
    /// Restores the local <c>bicep-local-docgen</c> tool (from the generated <c>.config/dotnet-tools.json</c>)
    /// and runs it against <c>src/Models</c>, writing markdown to <c>docs/</c>.
    /// </summary>
    public static async Task<DocsResult> GenerateAsync(string extensionDir, ExtensionModel model, CancellationToken ct = default)
    {
        var log = new System.Text.StringBuilder();

        var restore = await RunAsync("dotnet", "tool restore", extensionDir, ct);
        log.Append(restore.Output);
        if (!restore.Success)
            return new DocsResult(false, $"`dotnet tool restore` failed:{Environment.NewLine}{log}");

        // docgen writes each doc to docs/<resource-type lowercased>.md but does not create the nested
        // directories that a "/"-bearing type (a prefix or child path) implies. Pre-create them.
        PrecreateDocDirectories(extensionDir, model);

        var modelsDir = Path.Combine("src", "Models");
        var generate = await RunAsync("dotnet", $"bicep-local-docgen generate {modelsDir} --output docs --force", extensionDir, ct);
        log.Append(generate.Output);

        return new DocsResult(generate.Success, log.ToString());
    }

    private static void PrecreateDocDirectories(string extensionDir, ExtensionModel model)
    {
        var docsDir = Path.Combine(extensionDir, "docs");
        foreach (var resource in model.Resources)
        {
            if (!resource.EmitResourceType)
                continue;

            var relative = model.QualifiedResourceType(resource).ToLowerInvariant();
            if (!relative.Contains('/'))
                continue;

            var full = Path.Combine(docsDir, relative.Replace('/', Path.DirectorySeparatorChar) + ".md");
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        }
    }

    private static async Task<DocsResult> RunAsync(string fileName, string arguments, string workingDir, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = new Process { StartInfo = psi };
        var output = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new DocsResult(false, $"Failed to start '{fileName} {arguments}': {ex.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(ct);

        return new DocsResult(process.ExitCode == 0, output.ToString());
    }
}
