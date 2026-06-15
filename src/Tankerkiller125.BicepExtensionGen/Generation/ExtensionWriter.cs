using Tankerkiller125.BicepExtensionGen.Model;

namespace Tankerkiller125.BicepExtensionGen.Generation;

/// <summary>Writes a complete generated extension project to disk from an <see cref="ExtensionModel"/>.</summary>
public sealed class ExtensionWriter
{
    private readonly string _outputDir;
    private readonly string _rootNs;

    public ExtensionWriter(string outputDir, string rootNs)
    {
        _outputDir = outputDir;
        _rootNs = rootNs;
    }

    /// <summary>Returns the relative paths of every file written.</summary>
    public IReadOnlyList<string> Write(ExtensionModel ext)
    {
        var written = new List<string>();

        void Emit(string relativePath, string content)
        {
            var full = Path.Combine(_outputDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, GeneratedHeader.For(relativePath) + content);
            written.Add(relativePath);
        }

        // Static project files.
        Emit($"src/{ext.AssemblyName}.csproj", ProjectScaffolder.Csproj(ext, _rootNs));
        Emit("src/Program.cs", ProgramGenerator.ProgramFile(ext, _rootNs));
        Emit("src/Models/Configuration.cs", ProjectScaffolder.Configuration(ext, _rootNs));
        Emit("src/Handlers/ResourceHandlerBase.cs", HandlerGenerator.BaseClass(ext, _rootNs));
        Emit(".config/dotnet-tools.json", ProjectScaffolder.DotnetToolsJson());
        Emit(".gitignore", ProjectScaffolder.GitIgnore());
        Emit(".biceplocalgenignore", ProjectScaffolder.BicepLocalGenIgnore());
        Emit("build.ps1", ProjectScaffolder.BuildPs1(ext));
        Emit("script/publish.ps1", ProjectScaffolder.PublishPs1(ext));
        Emit("README.md", ProjectScaffolder.Readme(ext));

        // Shared enums + nested types (always emitted so the Models.Common namespace exists).
        Emit("src/Models/Common.cs", ModelGenerator.CommonFile(ext, _rootNs));

        // Per-resource models and handlers. A standalone resource is a flat file under Models/ in its
        // own namespace; a variant group lives under Models/<Group>/ with the parent base and variants
        // sharing that folder/namespace. The abstract parent base has no handler.
        foreach (var r in ext.Resources)
        {
            var modelPath = r.Group is null ? $"src/Models/{r.Name}.cs" : $"src/Models/{r.Group}/{r.Name}.cs";
            Emit(modelPath, ModelGenerator.ResourceFile(ext, r, _rootNs));
            if (r.EmitResourceType)
                Emit($"src/Handlers/{r.Name}Handler.cs", HandlerGenerator.HandlerFile(ext, r, _rootNs));
        }

        return written;
    }
}
