namespace Tankerkiller125.BicepExtensionGen.Tests;

/// <summary>Covers mapping of every OpenAPI 3.0 security scheme type and its generated wiring.</summary>
[TestClass]
public sealed class AuthTests
{
    private static async Task<ExtensionModel> BuildAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "auth.yaml");
        var load = await OpenApiLoader.LoadAsync(path);
        return new ResourceModelBuilder(load.Document, "Auth.Api", "0.1.0", MappingMode.Path).Build();
    }

    private static SecuritySchemeModel Scheme(ExtensionModel m, string key) => m.SecuritySchemes.Single(s => s.Key == key);

    [TestMethod]
    public async Task Maps_every_supported_scheme_kind()
    {
        var m = await BuildAsync();

        Assert.AreEqual(SecurityKind.ApiKeyHeader, Scheme(m, "headerKey").Kind);
        Assert.AreEqual("X-API-Key", Scheme(m, "headerKey").ParameterName);
        Assert.AreEqual(SecurityKind.ApiKeyQuery, Scheme(m, "queryKey").Kind);
        Assert.AreEqual(SecurityKind.ApiKeyCookie, Scheme(m, "cookieKey").Kind);
        Assert.AreEqual(SecurityKind.HttpBasic, Scheme(m, "basicAuth").Kind);
        Assert.AreEqual(SecurityKind.HttpBearer, Scheme(m, "bearerAuth").Kind);
        Assert.AreEqual(SecurityKind.OAuth2, Scheme(m, "oauth2Scheme").Kind);
        Assert.AreEqual(SecurityKind.OpenIdConnect, Scheme(m, "oidcScheme").Kind);
    }

    [TestMethod]
    public async Task Unsupported_scheme_is_skipped_with_a_warning()
    {
        var m = await BuildAsync();

        Assert.IsFalse(m.SecuritySchemes.Any(s => s.Key == "digestAuth"));
        Assert.IsTrue(m.Warnings.Any(w => w.Contains("digestAuth")));
    }

    [TestMethod]
    public async Task Basic_auth_has_username_and_password_properties()
    {
        var basic = Scheme(await BuildAsync(), "basicAuth");

        Assert.IsNotNull(basic.SecondaryProperty);
        Assert.AreNotEqual(basic.PrimaryProperty, basic.SecondaryProperty);
    }

    [TestMethod]
    public async Task Configuration_declares_optional_secure_credentials()
    {
        var m = await BuildAsync();
        var config = ProjectScaffolder.Configuration(m, "Auth.Api");

        // Every credential is an optional (nullable) property; secrets are marked secure.
        StringAssert.Contains(config, "public required string BaseUrl");
        StringAssert.Contains(config, "isSecure: true");
        StringAssert.Contains(config, "public string? " + Scheme(m, "headerKey").PrimaryProperty);
        StringAssert.Contains(config, "public string? " + Scheme(m, "basicAuth").SecondaryProperty);
    }

    [TestMethod]
    public async Task Base_class_applies_each_scheme_correctly()
    {
        var m = await BuildAsync();
        var baseClass = HandlerGenerator.BaseClass(m, "Auth.Api");

        StringAssert.Contains(baseClass, "TryAddWithoutValidation(\"X-API-Key\"");                 // header
        StringAssert.Contains(baseClass, "TryAddWithoutValidation(\"Cookie\", $\"session=");        // cookie
        StringAssert.Contains(baseClass, "new AuthenticationHeaderValue(\"Basic\"");                // basic
        StringAssert.Contains(baseClass, "new AuthenticationHeaderValue(\"Bearer\"");               // bearer/oauth2/oidc
        StringAssert.Contains(baseClass, "pairs.Add($\"api_key=");                                  // query
        StringAssert.Contains(baseClass, "ApplyQueryAuth(configuration");                           // query auth invoked
    }
}
