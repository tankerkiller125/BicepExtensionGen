namespace Tankerkiller125.BicepExtensionGen.Tests;

/// <summary>Coverage for excluding parts of a spec from generation via path/schema and tag globs.</summary>
[TestClass]
public sealed class ExclusionFilterTests
{
    private static async Task<ExtensionModel> BuildAsync(string fixture, ExclusionFilter filter, MappingMode mode = MappingMode.Path)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", fixture);
        var load = await OpenApiLoader.LoadAsync(path);
        return new ResourceModelBuilder(load.Document, "Test.Api", "0.1.0", mode, filter).Build();
    }

    private static List<string> Names(ExtensionModel m) => m.Resources.Select(r => r.Name).ToList();

    // ---- glob semantics --------------------------------------------------

    [TestMethod]
    public void Single_star_does_not_cross_slashes_double_star_does()
    {
        var single = new ExclusionFilter(["/accounts/*/gadgets"], []);
        Assert.IsTrue(single.MatchesName("/accounts/abc/gadgets"));
        Assert.IsFalse(single.MatchesName("/accounts/abc/def/gadgets"), "'*' must not cross '/'.");

        var deep = new ExclusionFilter(["**/ai/run/**"], []);
        Assert.IsTrue(deep.MatchesName("/accounts/{account_id}/ai/run/@cf/meta/llama"));
        Assert.IsFalse(deep.MatchesName("/accounts/{account_id}/dns_records"));
    }

    [TestMethod]
    public void Matching_is_case_insensitive()
    {
        Assert.IsTrue(new ExclusionFilter([], ["Workers AI*"]).MatchesTag("workers ai translation"));
    }

    // ---- path / tag exclusion (path mode) --------------------------------

    [TestMethod]
    public async Task Empty_filter_changes_nothing()
    {
        var baseline = Names(await BuildAsync("edgecases.yaml", ExclusionFilter.None));
        CollectionAssert.Contains(baseline, "Widget");
        CollectionAssert.Contains(baseline, "AccountGadget");
    }

    [TestMethod]
    public async Task Path_glob_excludes_matching_paths()
    {
        var names = Names(await BuildAsync("edgecases.yaml", new ExclusionFilter(["**/gadgets/**"], [])));

        CollectionAssert.DoesNotContain(names, "AccountGadget");
        CollectionAssert.DoesNotContain(names, "TeamGadget");
        CollectionAssert.Contains(names, "Widget", "Unrelated resources must survive.");
    }

    [TestMethod]
    public async Task Tag_glob_excludes_matching_operations()
    {
        // The /streams/{streamId} operations are tagged "streams".
        var names = Names(await BuildAsync("edgecases.yaml", new ExclusionFilter([], ["streams"])));

        CollectionAssert.DoesNotContain(names, "Stream2");
        CollectionAssert.Contains(names, "Widget");
    }

    [TestMethod]
    public async Task Excluding_a_union_path_drops_the_whole_variant_group()
    {
        var model = await BuildAsync("dns.yaml", new ExclusionFilter(["**/dns_records**"], []));
        var names = Names(model);

        Assert.IsFalse(names.Any(n => n.StartsWith("DnsRecord", StringComparison.Ordinal)),
            "Excluding the path must drop the parent base and every variant.");
        CollectionAssert.Contains(names, "Widget", "The unrelated enveloped resource must remain.");
    }

    // ---- schema mode -----------------------------------------------------

    [TestMethod]
    public async Task Name_glob_excludes_component_schemas_in_schema_mode()
    {
        var included = Names(await BuildAsync("edgecases.yaml", ExclusionFilter.None, MappingMode.Schema));
        CollectionAssert.Contains(included, "Widget");

        var filtered = Names(await BuildAsync("edgecases.yaml", new ExclusionFilter(["Widget"], []), MappingMode.Schema));
        CollectionAssert.DoesNotContain(filtered, "Widget");
        CollectionAssert.Contains(filtered, "Gadget", "Other schemas remain.");
    }
}
