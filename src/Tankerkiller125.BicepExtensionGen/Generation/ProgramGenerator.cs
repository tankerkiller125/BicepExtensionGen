using Tankerkiller125.BicepExtensionGen.Model;

namespace Tankerkiller125.BicepExtensionGen.Generation;

/// <summary>Emits the extension's <c>Program.cs</c> entry point that registers every handler.</summary>
public static class ProgramGenerator
{
    public static string ProgramFile(ExtensionModel ext, string rootNs)
    {
        var w = new CodeWriter();
        w.Line("using Bicep.Local.Extension.Host.Extensions;");
        w.Line("using Microsoft.AspNetCore.Builder;");
        w.Line("using Microsoft.Extensions.DependencyInjection;");
        w.Blank();

        w.Line("var builder = WebApplication.CreateBuilder();");
        w.Blank();
        w.Line("builder.AddBicepExtensionHost(args);");

        // Handler/Configuration types are referenced fully qualified: in the global namespace of
        // top-level statements, a `using` import would clash with BCL types for resources named
        // like "Event" (EventHandler vs System.EventHandler).
        w.Line("builder.Services");
        w.Line("    .AddBicepExtension(");
        w.Line($"        name: \"{ext.Name}\",");
        w.Line($"        version: \"{ext.Version}\",");
        w.Line("        isSingleton: true,");
        w.Line("        typeAssembly: typeof(Program).Assembly,");
        w.Line($"        configurationType: typeof({rootNs}.Models.Configuration))");
        foreach (var r in ext.Resources.Where(r => r.EmitResourceType))
            w.Line($"    .WithResourceHandler<{rootNs}.Handlers.{r.Name}Handler>()");
        w.Line("    ;");
        w.Blank();

        w.Line("var app = builder.Build();");
        w.Line("app.MapBicepExtension();");
        w.Line("await app.RunAsync();");
        return w.ToString();
    }
}
