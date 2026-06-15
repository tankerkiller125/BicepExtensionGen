using System.Text;
using Tankerkiller125.BicepExtensionGen.Model;

namespace Tankerkiller125.BicepExtensionGen.Generation;

/// <summary>Produces the static (non-resource-specific) files of a generated extension.</summary>
public static class ProjectScaffolder
{
    public const string ExtensionSdkVersion = "0.38.5";
    public const string LocalDeployVersion = "1.0.1";
    public const string DocGenVersion = "1.0.1";

    public static string Csproj(ExtensionModel ext, string rootNs) => $$"""
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <RootNamespace>{{rootNs}}</RootNamespace>
            <AssemblyName>{{ext.AssemblyName}}</AssemblyName>
            <TargetFramework>net9.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <InvariantGlobalization>true</InvariantGlobalization>
            <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
            <PublishSingleFile>true</PublishSingleFile>
            <SelfContained>true</SelfContained>
            <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
            <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
          </PropertyGroup>

          <ItemGroup>
            <PackageReference Include="Azure.Bicep.Local.Extension" Version="{{ExtensionSdkVersion}}" />
            <PackageReference Include="Bicep.LocalDeploy" Version="{{LocalDeployVersion}}" />
          </ItemGroup>

        </Project>

        """;

    public static string Configuration(ExtensionModel ext, string rootNs)
    {
        var w = new CodeWriter();
        w.Line("using Azure.Bicep.Types.Concrete;");
        w.Line("using Bicep.Local.Extension.Types.Attributes;");
        w.Blank();
        w.Line($"namespace {rootNs}.Models;");
        w.Blank();
        w.Line("/// <summary>Extension configuration supplied via <c>extension ... with { ... }</c> in Bicep.</summary>");
        using (w.Block("public class Configuration"))
        {
            w.Line("[TypeProperty(\"The base URL for the API endpoint.\", ObjectTypePropertyFlags.Required)]");
            w.Line("public required string BaseUrl { get; set; }");

            foreach (var s in ext.SecuritySchemes)
            {
                w.Blank();
                if (s.Kind == SecurityKind.HttpBasic)
                {
                    WriteCredential(w, s.PrimaryProperty, $"Username for the '{s.Key}' HTTP Basic scheme.", secure: false);
                    w.Blank();
                    WriteCredential(w, s.SecondaryProperty!, $"Password for the '{s.Key}' HTTP Basic scheme.", secure: true);
                }
                else
                {
                    WriteCredential(w, s.PrimaryProperty, s.Description, secure: true);
                }
            }
        }

        return w.ToString();
    }

    private static void WriteCredential(CodeWriter w, string property, string description, bool secure)
    {
        var secureArg = secure ? ", isSecure: true" : "";
        w.Line($"[TypeProperty(\"{description.Replace("\"", "\\\"")}\"{secureArg})]");
        w.Line($"public string? {property} {{ get; set; }}");
    }

    public static string DotnetToolsJson() => $$"""
        {
          "version": 1,
          "isRoot": true,
          "tools": {
            "bicep-local-docgen": {
              "version": "{{DocGenVersion}}",
              "commands": [
                "bicep-local-docgen"
              ]
            }
          }
        }

        """;

    public static string GitIgnore() => """
        bin/
        obj/
        output/
        *.user
        """;

    public static string BicepLocalGenIgnore() => """
        # Files and directories excluded from documentation generation.
        **/bin/**
        **/obj/**
        **/.git/**
        **/.vs/**
        **/.vscode/**
        Configuration.cs
        """;

    public static string BuildPs1(ExtensionModel ext) => """
        #Requires -Version 7.0
        <#
        .SYNOPSIS
        Builds the extension and packages it for win-x64, linux-x64 and osx-x64 via `bicep publish-extension`.
        #>
        [CmdletBinding()]
        param([ValidateSet('Release', 'Debug')][string]$Configuration = 'Release')

        $ErrorActionPreference = 'Stop'
        $root = $PSScriptRoot
        $src = Join-Path $root 'src'
        $output = Join-Path $root 'output'

        $csproj = Get-ChildItem -Path $src -Filter *.csproj -File | Select-Object -First 1
        if (-not $csproj) { throw "No .csproj found under $src" }

        [xml]$xml = Get-Content $csproj.FullName
        $exeName = ($xml.Project.PropertyGroup.AssemblyName | Where-Object { $_ }) | Select-Object -First 1

        Write-Host "Building $($csproj.Name) ($Configuration)..."
        dotnet build $csproj.FullName -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

        $platforms = @('win-x64', 'linux-x64', 'osx-x64')
        $extArgs = @('publish-extension')
        foreach ($platform in $platforms) {
            $platformOut = Join-Path $output $platform
            Write-Host "Publishing for $platform..."
            dotnet publish $csproj.FullName -c $Configuration -r $platform -o $platformOut
            if ($LASTEXITCODE -ne 0) { throw "Publish failed for $platform." }
            $bin = if ($platform -eq 'win-x64') { Join-Path $platformOut "$exeName.exe" } else { Join-Path $platformOut $exeName }
            $extArgs += @("--bin-$platform", $bin)
        }

        $extArgs += @('--target', (Join-Path $output $exeName), '--force')

        if (Get-Command bicep -ErrorAction SilentlyContinue) {
            Write-Host 'Packaging extension with bicep publish-extension...'
            bicep @extArgs
        }
        else {
            Write-Warning 'Bicep CLI not found; skipping publish-extension. Install from https://aka.ms/bicep/install'
        }
        """;

    public static string PublishPs1(ExtensionModel ext) => """
        #Requires -Version 7.0
        <#
        .SYNOPSIS
        Publishes the extension to an OCI container registry (e.g. Azure Container Registry).
        Make sure you are logged in to the registry before running.
        #>
        [CmdletBinding()]
        param(
            [Parameter(Mandatory = $true)][string]$RegistryUrl,
            [Parameter(Mandatory = $true)][string]$Tag,
            [string]$Repository = 'bicep-extension',
            [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release'
        )

        $ErrorActionPreference = 'Stop'
        $root = $PSScriptRoot
        $src = Join-Path $root 'src'
        $output = Join-Path $root 'output'

        $csproj = Get-ChildItem -Path $src -Filter *.csproj -File | Select-Object -First 1
        [xml]$xml = Get-Content $csproj.FullName
        $exeName = ($xml.Project.PropertyGroup.AssemblyName | Where-Object { $_ }) | Select-Object -First 1

        $platforms = @('win-x64', 'linux-x64', 'osx-x64')
        $extArgs = @('publish-extension')
        foreach ($platform in $platforms) {
            $platformOut = Join-Path $output $platform
            dotnet publish $csproj.FullName -c $Configuration -r $platform -o $platformOut
            $bin = if ($platform -eq 'win-x64') { Join-Path $platformOut "$exeName.exe" } else { Join-Path $platformOut $exeName }
            $extArgs += @("--bin-$platform", $bin)
        }

        $target = "br:$RegistryUrl/$Repository:$Tag"
        $extArgs += @('--target', $target, '--force')
        Write-Host "Publishing extension to $target"
        bicep @extArgs
        """;

    public static string Readme(ExtensionModel ext)
    {
        var sb = new StringBuilder();
        var camelName = NameUtil.Camel(ext.Name).TrimStart('@');

        sb.Append("# ").Append(ext.Name).Append("\n\n");
        sb.Append("A .NET Bicep extension generated from an OpenAPI 3.0 definition by Tankerkiller125.BicepExtensionGen.\n\n");

        // ---- How to work with the project itself (comes first) ----
        sb.Append("## Working with this project\n\n");
        sb.Append("This repository is a self-contained .NET Bicep extension. Build and publish it as follows.\n\n");

        sb.Append("### Prerequisites\n\n");
        sb.Append("- .NET SDK 9.0 or later\n");
        sb.Append("- [Bicep CLI](https://aka.ms/bicep/install) (to package and publish the extension)\n\n");

        sb.Append("### Build & package\n\n");
        sb.Append("```bash\npwsh ./build.ps1\n```\n\n");
        sb.Append("Builds the project, runs tests, publishes self-contained binaries for `win-x64`, `linux-x64`, ");
        sb.Append("and `osx-x64`, then packages the extension with `bicep publish-extension`.\n\n");

        sb.Append("### Publish to a container registry\n\n");
        sb.Append("```bash\npwsh ./script/publish.ps1 -RegistryUrl <registry>.azurecr.io -Tag ").Append(ext.Version).Append("\n```\n\n");

        sb.Append("### Regenerate reference documentation\n\n");
        sb.Append("```bash\ndotnet tool restore\ndotnet bicep-local-docgen generate src/Models --output docs --force\n```\n\n");
        sb.Append("Per-resource reference docs are written to `docs/`.\n\n");

        // ---- Using the generated extension (auth is the only OpenAPI-derived content kept) ----
        sb.Append("## Using the extension\n\n");
        AppendAuthentication(sb, ext, camelName);

        sb.Append("### Quick start\n\n");
        sb.Append("```bicep\ntargetScope = 'local'\n\n");
        sb.Append(CredentialParams(ext));
        sb.Append(ExtensionBlock(ext, camelName));
        var deployable = ext.Resources.Where(r => r.EmitResourceType).ToList();
        if (deployable.Count > 0)
            sb.Append('\n').Append(ExampleGenerator.ResourceDeclaration(deployable[0], ext.QualifiedResourceType(deployable[0])));
        sb.Append("```\n\n");

        sb.Append("### Resources\n\n");
        foreach (var r in deployable)
        {
            sb.Append("#### ").Append(ext.QualifiedResourceType(r)).Append("\n\n");
            sb.Append("```bicep\n").Append(ExampleGenerator.ResourceDeclaration(r, ext.QualifiedResourceType(r))).Append("```\n\n");
        }

        sb.Append("> Generated by [Tankerkiller125.BicepExtensionGen](https://github.com/) from an OpenAPI 3.0 document.\n");
        return sb.ToString();
    }

    /// <summary>Writes the authentication section, with guidance specific to each detected scheme.</summary>
    private static void AppendAuthentication(StringBuilder sb, ExtensionModel ext, string camelName)
    {
        sb.Append("### Authentication\n\n");

        if (ext.SecuritySchemes.Count == 0)
        {
            sb.Append("This API does not declare any authentication in its OpenAPI document. ");
            sb.Append("If it nonetheless requires credentials, add them in `src/Models/Configuration.cs` ");
            sb.Append("and apply them in `src/Handlers/ResourceHandlerBase.cs`.\n\n");
            return;
        }

        sb.Append("Credentials are supplied through the extension `with { ... }` block. ");
        if (ext.SecuritySchemes.Count > 1)
            sb.Append("This API defines multiple schemes — provide whichever it requires; only the credentials you set are applied. ");
        sb.Append("Secrets are marked `@secure`, so pass them via secure parameters rather than hard-coding them.\n\n");

        foreach (var s in ext.SecuritySchemes)
        {
            sb.Append("- ").Append(AuthInstruction(s)).Append('\n');
        }
        sb.Append('\n');
    }

    /// <summary>A one-line, method-specific instruction for configuring a scheme.</summary>
    private static string AuthInstruction(SecuritySchemeModel s)
    {
        var prop = NameUtil.Camel(s.PrimaryProperty).TrimStart('@');
        return s.Kind switch
        {
            SecurityKind.ApiKeyHeader => $"**{s.Key}** — set `{prop}` to your API key; it is sent in the `{s.ParameterName}` request header.",
            SecurityKind.ApiKeyQuery => $"**{s.Key}** — set `{prop}` to your API key; it is appended as the `{s.ParameterName}` query parameter.",
            SecurityKind.ApiKeyCookie => $"**{s.Key}** — set `{prop}` to your API key; it is sent in the `{s.ParameterName}` cookie.",
            SecurityKind.HttpBasic => $"**{s.Key}** — set `{prop}` and `{NameUtil.Camel(s.SecondaryProperty!).TrimStart('@')}`; they are sent as HTTP Basic credentials.",
            SecurityKind.HttpBearer => $"**{s.Key}** — set `{prop}` to your bearer token; it is sent as `Authorization: Bearer`.",
            SecurityKind.OAuth2 => $"**{s.Key}** — set `{prop}` to an OAuth 2.0 access token; it is sent as `Authorization: Bearer`.",
            SecurityKind.OpenIdConnect => $"**{s.Key}** — set `{prop}` to an OpenID Connect token; it is sent as `Authorization: Bearer`.",
            _ => $"**{s.Key}** — {s.Description}",
        };
    }

    /// <summary>Secure parameter declarations for each credential referenced by the quick start.</summary>
    private static string CredentialParams(ExtensionModel ext)
    {
        var sb = new StringBuilder();
        foreach (var p in CredentialProperties(ext))
            sb.Append("@secure()\nparam ").Append(p).Append(" string\n\n");
        return sb.ToString();
    }

    /// <summary>The <c>extension … with { … }</c> block, wiring baseUrl and each credential parameter.</summary>
    private static string ExtensionBlock(ExtensionModel ext, string camelName)
    {
        var sb = new StringBuilder();
        sb.Append("extension ").Append(camelName).Append(" with {\n");
        sb.Append("  baseUrl: '").Append(ext.BaseUrl ?? "https://api.example.com").Append("'\n");
        foreach (var p in CredentialProperties(ext))
            sb.Append("  ").Append(p).Append(": ").Append(p).Append('\n');
        sb.Append("}\n");
        return sb.ToString();
    }

    /// <summary>The camelCase config/credential property names contributed by all security schemes.</summary>
    private static IEnumerable<string> CredentialProperties(ExtensionModel ext)
    {
        foreach (var s in ext.SecuritySchemes)
        {
            yield return NameUtil.Camel(s.PrimaryProperty).TrimStart('@');
            if (s.SecondaryProperty is not null)
                yield return NameUtil.Camel(s.SecondaryProperty).TrimStart('@');
        }
    }
}
