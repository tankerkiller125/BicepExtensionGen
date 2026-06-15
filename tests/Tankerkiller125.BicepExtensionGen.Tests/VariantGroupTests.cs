namespace Tankerkiller125.BicepExtensionGen.Tests;

/// <summary>
/// Coverage for the two Cloudflare-shaped fixes: unwrapping <c>{ result, success, ... }</c> response
/// envelopes, and expanding a discriminated-union write body (e.g. a DNS record's per-type variants)
/// into one resource per variant grouped under a shared abstract parent base.
/// </summary>
[TestClass]
public sealed class VariantGroupTests
{
    private static async Task<ExtensionModel> BuildAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "dns.yaml");
        var load = await OpenApiLoader.LoadAsync(path);
        return new ResourceModelBuilder(load.Document, "Example.Dns", "0.1.0", MappingMode.Path).Build();
    }

    private static ResourceModel Resource(ExtensionModel m, string name) => m.Resources.Single(r => r.Name == name);

    // ---- envelope unwrapping --------------------------------------------

    [TestMethod]
    public async Task Result_envelope_is_unwrapped_to_the_inner_object()
    {
        var widget = Resource(await BuildAsync(), "Widget");
        var names = widget.Properties.Select(p => p.JsonName).ToList();

        // The real object's fields surface...
        CollectionAssert.Contains(widget.Writable.Select(p => p.Name).ToList(), "Name");
        CollectionAssert.Contains(widget.Writable.Select(p => p.Name).ToList(), "Color");

        // ...and the envelope wrapper fields do not.
        CollectionAssert.DoesNotContain(names, "result");
        CollectionAssert.DoesNotContain(names, "success");
        CollectionAssert.DoesNotContain(names, "errors");
    }

    // ---- variant group shape --------------------------------------------

    [TestMethod]
    public async Task Union_write_body_expands_into_one_resource_per_variant()
    {
        var model = await BuildAsync();
        var group = model.Resources.Where(r => r.Group == "DnsRecord").Select(r => r.Name).ToList();

        // Abstract parent base + one resource per record type (case of the type preserved).
        CollectionAssert.AreEquivalent(
            new[] { "DnsRecord", "DnsRecordA", "DnsRecordTXT", "DnsRecordCAA" },
            group);
    }

    [TestMethod]
    public async Task Parent_base_is_an_abstract_non_resource_owning_the_identifiers()
    {
        var parent = Resource(await BuildAsync(), "DnsRecord");

        Assert.IsTrue(parent.IsAbstract);
        Assert.IsFalse(parent.EmitResourceType, "The base must not be a deployable resource.");
        Assert.IsTrue(parent.OwnsIdentifiers);
        Assert.AreEqual("DnsRecordIdentifiers", parent.EffectiveBaseType);

        // Shared writable fields and read-only outputs live on the base.
        CollectionAssert.IsSubsetOf(
            new[] { "Name", "Ttl", "Comment", "Proxied" },
            parent.Writable.Select(p => p.Name).ToList());
        CollectionAssert.Contains(parent.Outputs.Select(p => p.Name).ToList(), "CreatedOn");

        // Both the parent zone key and the record's own id are identifiers.
        CollectionAssert.AreEquivalent(new[] { "ZoneId", "Id" }, parent.Identifiers.Select(p => p.Name).ToList());
    }

    [TestMethod]
    public async Task Variant_derives_from_base_and_shares_its_identifiers()
    {
        var a = Resource(await BuildAsync(), "DnsRecordA");

        Assert.AreEqual("DnsRecord", a.Group);
        Assert.AreEqual("DnsRecord", a.EffectiveBaseType, "A variant derives from the group's parent base.");
        Assert.IsFalse(a.OwnsIdentifiers, "A variant inherits its identifiers from the base.");
        Assert.AreEqual("DnsRecordIdentifiers", a.EffectiveIdentifiersType);
        Assert.IsTrue(a.EmitResourceType);
    }

    [TestMethod]
    public async Task Variant_carries_its_own_fields_and_inherits_shared_ones()
    {
        var a = Resource(await BuildAsync(), "DnsRecordA");

        // Variant-specific fields are declared on the variant (not inherited)...
        var content = a.Properties.Single(p => p.Name == "Content");
        Assert.IsFalse(content.Inherited);
        Assert.IsFalse(content.IsReadOnly, "An A record's content is a writable input.");

        var type = a.Properties.Single(p => p.Name == "Type");
        Assert.IsFalse(type.Inherited);
        Assert.IsTrue(type.IsRequired, "The discriminator must be set.");
        Assert.IsTrue(type.Type.IsEnum);

        // ...while shared fields are present but marked inherited (emitted on the base, not the variant).
        var name = a.Properties.Single(p => p.Name == "Name");
        Assert.IsTrue(name.Inherited);
    }

    [TestMethod]
    public async Task Variant_with_data_keeps_content_read_only_and_data_required()
    {
        var caa = Resource(await BuildAsync(), "DnsRecordCAA");

        var content = caa.Properties.Single(p => p.Name == "Content");
        Assert.IsTrue(content.IsReadOnly, "A CAA record's content is server-computed.");

        var data = caa.Properties.Single(p => p.Name == "Data");
        Assert.IsFalse(data.Inherited);
        Assert.IsFalse(data.IsReadOnly);
        Assert.IsTrue(data.Type.Name.StartsWith("DnsRecordCAA", StringComparison.Ordinal), "Data maps to a generated nested type.");
    }

    [TestMethod]
    public async Task Variants_share_the_crud_endpoints_and_the_base_has_none()
    {
        var model = await BuildAsync();

        var parent = Resource(model, "DnsRecord");
        Assert.IsNull(parent.Ops.Create);
        Assert.IsNull(parent.Ops.Read);

        var a = Resource(model, "DnsRecordA");
        Assert.AreEqual("zones/{zone_id}/dns_records", a.Ops.Create!.PathTemplate);
        Assert.AreEqual("zones/{zone_id}/dns_records/{dns_record_id}", a.Ops.Read!.PathTemplate);
        Assert.AreEqual("Put", a.Ops.Update!.Method);
        Assert.IsNotNull(a.Ops.Delete);
    }

    // ---- when NOT to split a union --------------------------------------

    [TestMethod]
    public async Task Response_only_union_stays_a_single_resource()
    {
        var model = await BuildAsync();

        // /zones/{zone_id}/clear_status has no write body — the union is only in the response, so it
        // must not fan out into per-state resources.
        var names = model.Resources.Select(r => r.Name).ToList();
        CollectionAssert.Contains(names, "ClearStatus");
        Assert.IsFalse(model.Resources.Any(r => r.Group == "ClearStatus"),
            "A response-only (status) union must not become a variant group.");
        Assert.IsFalse(names.Any(n => n.Contains("Variant", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Anonymous_inline_union_body_stays_a_single_resource()
    {
        var model = await BuildAsync();

        // blob_post is anyOf of two anonymous inline objects: no discriminator, no $ref names, so the
        // variants can't be named — it must collapse to one resource rather than Blob/Blob2/...
        CollectionAssert.Contains(model.Resources.Select(r => r.Name).ToList(), "Blob");
        Assert.IsFalse(model.Resources.Any(r => r.Group == "Blob"));
        Assert.IsTrue(model.Warnings.Any(w => w.Contains("Blob", StringComparison.Ordinal)),
            "An un-nameable union body should warn that it was emitted as a single resource.");
    }

    [TestMethod]
    public async Task Same_leaf_groups_disambiguate_by_path_not_number()
    {
        var model = await BuildAsync();
        var names = model.Resources.Select(r => r.Name).ToList();

        // The two /things union endpoints become AccountThing / ZoneThing groups...
        CollectionAssert.Contains(names, "AccountThing");
        CollectionAssert.Contains(names, "ZoneThing");
        // ...not a bare "Thing" plus a numeric "Thing2".
        CollectionAssert.DoesNotContain(names, "Thing");
        CollectionAssert.DoesNotContain(names, "Thing2");

        // The rename cascades cleanly to the variants and their base/group.
        var variant = Resource(model, "AccountThingA");
        Assert.AreEqual("AccountThing", variant.Group);
        Assert.AreEqual("AccountThing", variant.EffectiveBaseType);
        Assert.AreEqual("AccountThingIdentifiers", variant.EffectiveIdentifiersType);
        Assert.IsTrue(Resource(model, "AccountThing").IsAbstract);
    }

    // ---- emission -------------------------------------------------------

    [TestMethod]
    public async Task Emits_variants_under_a_group_folder_with_no_base_handler()
    {
        var model = await BuildAsync();
        var dir = Path.Combine(Path.GetTempPath(), "bicepextgen-dns-" + Guid.NewGuid().ToString("N"));
        try
        {
            new ExtensionWriter(dir, "Example.Dns").Write(model);

            // Variants and the parent base share a Models/DnsRecord/ folder.
            Assert.IsTrue(File.Exists(Path.Combine(dir, "src/Models/DnsRecord/DnsRecord.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(dir, "src/Models/DnsRecord/DnsRecordA.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(dir, "src/Models/DnsRecord/DnsRecordCAA.cs")));

            // The abstract base is not a deployable resource: no handler, no registration.
            Assert.IsFalse(File.Exists(Path.Combine(dir, "src/Handlers/DnsRecordHandler.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(dir, "src/Handlers/DnsRecordAHandler.cs")));

            var program = await File.ReadAllTextAsync(Path.Combine(dir, "src/Program.cs"));
            StringAssert.Contains(program, "DnsRecordAHandler");
            StringAssert.DoesNotMatch(program, new System.Text.RegularExpressions.Regex(@"WithResourceHandler<[^>]*\.DnsRecordHandler>"));

            // The variant class derives from the abstract base in the shared namespace.
            var variant = await File.ReadAllTextAsync(Path.Combine(dir, "src/Models/DnsRecord/DnsRecordA.cs"));
            StringAssert.Contains(variant, "namespace Example.Dns.Models.DnsRecord;");
            StringAssert.Contains(variant, "public class DnsRecordA : DnsRecord");
            StringAssert.Contains(variant, "[ResourceType(\"DnsRecordA\")]");

            var baseModel = await File.ReadAllTextAsync(Path.Combine(dir, "src/Models/DnsRecord/DnsRecord.cs"));
            StringAssert.Contains(baseModel, "public abstract class DnsRecord : DnsRecordIdentifiers");
            StringAssert.Contains(baseModel, "public class DnsRecordIdentifiers");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
