namespace Tankerkiller125.BicepExtensionGen.Tests;

/// <summary>
/// Regressions for issues found generating from large real-world specs (e.g. the Cloudflare API):
/// cyclic <c>$ref</c>, BCL name collisions, duplicate enum members, and resources missing verbs.
/// </summary>
[TestClass]
public sealed class EdgeCaseTests
{
    private static async Task<ExtensionModel> BuildAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "edgecases.yaml");
        var load = await OpenApiLoader.LoadAsync(path);
        return new ResourceModelBuilder(load.Document, "Edge.Api", "0.1.0", MappingMode.Path).Build();
    }

    [TestMethod]
    public async Task Cyclic_reference_terminates_and_creates_a_single_node_type()
    {
        // Completing at all proves the cycle is broken; Node should map to one nested type.
        var model = await BuildAsync();

        Assert.IsTrue(model.NestedTypes.Any(t => t.Name == "Node"));
    }

    [TestMethod]
    public async Task Resource_colliding_with_bcl_type_is_renamed()
    {
        var model = await BuildAsync();

        Assert.IsFalse(model.Resources.Any(r => r.Name == "Stream"), "A resource named 'Stream' would clash with System.IO.Stream.");
        Assert.IsTrue(model.Resources.Any(r => r.Name == "Stream2"));
    }

    [TestMethod]
    public async Task Duplicate_pascalized_enum_members_are_unique()
    {
        var model = await BuildAsync();
        var mode = model.Enums.Single(e => e.Values.Contains("Read"));

        Assert.AreEqual(mode.Values.Count, mode.Values.Distinct().Count(), "Enum member names must be unique.");
        Assert.AreEqual(mode.Values.Count, mode.JsonValues.Count, "Member names and JSON values must stay aligned.");
    }

    [TestMethod]
    public async Task Handler_without_create_or_update_only_references_emitted_methods()
    {
        var model = await BuildAsync();
        var stream = model.Resources.Single(r => r.Name == "Stream2");

        // The /streams path has only GET + DELETE, so no create/update helpers may be referenced.
        Assert.IsNull(stream.Ops.Create);
        Assert.IsNull(stream.Ops.Update);

        var handler = HandlerGenerator.HandlerFile(model, stream, "Edge.Api");
        StringAssert.DoesNotMatch(handler, new System.Text.RegularExpressions.Regex(@"await CreateResourceAsync"));
        StringAssert.DoesNotMatch(handler, new System.Text.RegularExpressions.Regex(@"await UpdateResourceAsync"));
    }

    [TestMethod]
    public async Task Colliding_leaf_names_are_disambiguated_by_parent_path()
    {
        var model = await BuildAsync();
        var names = model.Resources.Select(r => r.Name).ToList();

        // Two "/gadgets" collections under different parents must not become Gadget / Gadget2.
        CollectionAssert.Contains(names, "AccountGadget");
        CollectionAssert.Contains(names, "TeamGadget");
        CollectionAssert.DoesNotContain(names, "Gadget");
        CollectionAssert.DoesNotContain(names, "Gadget2");
    }

    [TestMethod]
    public async Task Unique_leaf_name_stays_short()
    {
        var model = await BuildAsync();
        // "/widgets" has no sibling collision, so it should remain "Widget" (not qualified/suffixed).
        CollectionAssert.Contains(model.Resources.Select(r => r.Name).ToList(), "Widget");
    }

    [TestMethod]
    public async Task Resource_name_never_collides_with_a_generated_type()
    {
        var model = await BuildAsync();
        var typeNames = model.NestedTypes.Select(t => t.Name)
            .Concat(model.Enums.Select(e => e.Name))
            .ToHashSet();

        // No resource may share a name with a nested type/enum (would break handler references).
        Assert.IsFalse(model.Resources.Any(r => typeNames.Contains(r.Name)));
    }

    [TestMethod]
    public async Task Nested_resource_carries_parent_identifier_first_with_clear_description()
    {
        var model = await BuildAsync();
        var gadget = model.Resources.Single(r => r.Name == "AccountGadget");
        var identifiers = gadget.Identifiers.ToList();

        // Both the parent key and the resource's own key are required identifiers.
        Assert.AreEqual(2, identifiers.Count);
        Assert.IsTrue(identifiers.All(i => i.IsRequired));

        // The parent key comes first, is clearly described, and substitutes into the path template.
        var parent = identifiers[0];
        Assert.AreEqual("AccountId", parent.Name);
        Assert.AreEqual("accountId", parent.PathParamName);
        StringAssert.Contains(parent.Description, "parent Account");

        // The handler URL is built from both keys (parent first).
        var handler = HandlerGenerator.HandlerFile(model, gadget, "Edge.Api");
        StringAssert.Contains(handler, "accounts/{Uri.EscapeDataString(props.AccountId.ToString()!)}/gadgets/");
    }

    [TestMethod]
    public async Task Bearer_security_scheme_is_detected()
    {
        var model = await BuildAsync();
        Assert.AreEqual(SecurityKind.HttpBearer, model.SecuritySchemes.Single().Kind);
    }
}
