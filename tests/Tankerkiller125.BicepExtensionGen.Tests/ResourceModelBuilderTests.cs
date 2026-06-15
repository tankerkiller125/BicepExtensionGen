namespace Tankerkiller125.BicepExtensionGen.Tests;

[TestClass]
public sealed class ResourceModelBuilderTests
{
    private static async Task<ExtensionModel> BuildPetStoreAsync(MappingMode mode = MappingMode.Path)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "petstore.yaml");
        var load = await OpenApiLoader.LoadAsync(path);
        return new ResourceModelBuilder(load.Document, "Contoso.PetStore", "0.1.0", mode).Build();
    }

    [TestMethod]
    public async Task Discovers_single_pet_resource()
    {
        var model = await BuildPetStoreAsync();

        Assert.AreEqual(1, model.Resources.Count);
        Assert.AreEqual("Pet", model.Resources[0].Name);
        Assert.AreEqual("Pet", model.Resources[0].ResourceTypeName);
        Assert.AreEqual("pets", model.Resources[0].Category);
    }

    [TestMethod]
    public async Task Path_parameter_becomes_required_identifier()
    {
        var model = await BuildPetStoreAsync();
        var id = model.Resources[0].Identifiers.Single();

        Assert.AreEqual("Id", id.Name);
        Assert.AreEqual("long", id.Type.Name);
        Assert.IsTrue(id.IsRequired);
        Assert.IsFalse(id.IsReadOnly, "An identifier must be writable even if the schema marks it readOnly.");
        Assert.AreEqual("petId", id.PathParamName);
    }

    [TestMethod]
    public async Task Maps_crud_operations_to_endpoints()
    {
        var ops = (await BuildPetStoreAsync()).Resources[0].Ops;

        Assert.AreEqual("pets", ops.Create!.PathTemplate);
        Assert.AreEqual("pets/{petId}", ops.Read!.PathTemplate);
        Assert.AreEqual("Put", ops.Update!.Method);
        Assert.AreEqual("pets/{petId}", ops.Delete!.PathTemplate);
    }

    [TestMethod]
    public async Task Marks_readonly_response_only_properties_as_outputs()
    {
        var pet = (await BuildPetStoreAsync()).Resources[0];
        var createdAt = pet.Properties.Single(p => p.Name == "CreatedAt");

        Assert.IsTrue(createdAt.IsReadOnly);
        Assert.AreEqual("string", createdAt.Type.Name);
        CollectionAssert.Contains(pet.Outputs.Select(p => p.Name).ToList(), "CreatedAt");
    }

    [TestMethod]
    public async Task Generates_enum_from_string_enum_schema()
    {
        var model = await BuildPetStoreAsync();
        var status = model.Enums.Single(e => e.Name == "PetStatus");

        CollectionAssert.AreEqual(new[] { "Available", "Pending", "Sold" }, status.Values);
        CollectionAssert.AreEqual(new[] { "available", "pending", "sold" }, status.JsonValues);
    }

    [TestMethod]
    public async Task Generates_nested_type_from_object_property()
    {
        var model = await BuildPetStoreAsync();
        var metadata = model.NestedTypes.Single(t => t.Name == "PetMetadata");

        CollectionAssert.AreEquivalent(
            new[] { "MicrochipId", "RegisteredBy" },
            metadata.Properties.Select(p => p.Name).ToList());
    }

    [TestMethod]
    public async Task Derives_base_url_and_api_key_auth()
    {
        var model = await BuildPetStoreAsync();

        Assert.AreEqual("https://api.petstore.example.com/v1", model.BaseUrl);
        var scheme = model.SecuritySchemes.Single();
        Assert.AreEqual(SecurityKind.ApiKeyHeader, scheme.Kind);
        Assert.AreEqual("X-API-Key", scheme.ParameterName);
    }

    [TestMethod]
    public async Task Schema_mapping_mode_produces_a_resource_per_schema()
    {
        var model = await BuildPetStoreAsync(MappingMode.Schema);

        Assert.IsTrue(model.Resources.Any(r => r.Name == "Pet"));
    }
}
