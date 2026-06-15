namespace Tankerkiller125.BicepExtensionGen.Tests;

/// <summary>Coverage for the Azure-style resource-type prefix (e.g. "Cloudflare.Dns/DnsRecordA").</summary>
[TestClass]
public sealed class ResourceTypePrefixTests
{
    private static async Task<ExtensionModel> BuildAsync(string? prefix, bool path = false, string fixture = "petstore.yaml")
    {
        var file = Path.Combine(AppContext.BaseDirectory, "fixtures", fixture);
        var load = await OpenApiLoader.LoadAsync(file);
        return new ResourceModelBuilder(load.Document, "Contoso.PetStore", "0.1.0", MappingMode.Path, filter: null, resourceTypePrefix: prefix, useResourceTypePath: path).Build();
    }

    [TestMethod]
    public async Task No_prefix_leaves_type_names_bare()
    {
        var model = await BuildAsync(null);
        Assert.AreEqual("Pet", model.QualifiedResourceType(model.Resources[0]));
    }

    [TestMethod]
    public async Task Prefix_qualifies_the_type_string_but_not_the_class_name()
    {
        var model = await BuildAsync("Contoso.Pets");
        var pet = model.Resources.Single(r => r.Name == "Pet");

        Assert.AreEqual("Contoso.Pets/Pet", model.QualifiedResourceType(pet));

        var source = ModelGenerator.ResourceFile(model, pet, "Contoso.PetStore");
        StringAssert.Contains(source, "[ResourceType(\"Contoso.Pets/Pet\")]");
        // The C# class/identifiers names stay unprefixed.
        StringAssert.Contains(source, "public class Pet : PetIdentifiers");
        // The doc example uses the qualified type.
        StringAssert.Contains(source, "resource pet 'Contoso.Pets/Pet' = {");
    }

    [TestMethod]
    public async Task Trailing_slash_and_whitespace_are_trimmed()
    {
        var model = await BuildAsync("  Contoso.Pets/  ");
        Assert.AreEqual("Contoso.Pets/Pet", model.QualifiedResourceType(model.Resources[0]));
    }

    // ---- hierarchical child paths ---------------------------------------

    [TestMethod]
    public async Task Child_path_uses_the_full_path_hierarchy()
    {
        // The DNS fixture's records live under /zones/{zone_id}/dns_records → Zone/DnsRecord, and each
        // variant adds its discriminator as a child segment.
        var model = await BuildAsync(prefix: null, path: true, fixture: "dns.yaml");
        var a = model.Resources.Single(r => r.Name == "DnsRecordA");

        Assert.AreEqual("Zone/DnsRecord/A", model.QualifiedResourceType(a));
        // The flat C# class name is unaffected by child-path typing.
        Assert.AreEqual("DnsRecordA", a.Name);
    }

    [TestMethod]
    public async Task Child_path_composes_with_a_prefix()
    {
        var model = await BuildAsync(prefix: "Cloudflare", path: true, fixture: "dns.yaml");
        var a = model.Resources.Single(r => r.Name == "DnsRecordA");

        Assert.AreEqual("Cloudflare/Zone/DnsRecord/A", model.QualifiedResourceType(a));
    }

    [TestMethod]
    public async Task Same_path_resources_get_unique_child_paths()
    {
        // Two /things union endpoints (account + zone) yield distinct hierarchies, no numeric clash.
        var model = await BuildAsync(prefix: null, path: true, fixture: "dns.yaml");
        var types = model.Resources
            .Where(r => r.EmitResourceType)
            .Select(model.QualifiedResourceType)
            .ToList();

        Assert.AreEqual(types.Count, types.Distinct().Count(), "Child-path types must be globally unique.");
        CollectionAssert.Contains(types, "Account/Thing/A");
        CollectionAssert.Contains(types, "Zone/Thing/A");
    }
}
