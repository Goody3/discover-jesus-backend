using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using DiscoverJesus.Api.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiscoverJesus.Api.Tests;

public sealed class OpenApiContractTests
{
    public static IEnumerable<object[]> ResponseExamples()
    {
        var json = JsonSerializer.SerializeToElement<object?>(null);
        var binding = new BindingResponse(0, "", "", json);
        var action = new ActionResponse(0, "", "", json);
        var component = new ComponentResponse(0, "", "", json, 0, [binding], [action]);
        yield return [new HealthResponse("healthy")];
        yield return [new PageSummary(0, Guid.Empty, "", "", false)];
        yield return [new PublishedPageSummary(Guid.Empty, "", "")];
        yield return [new PageResponse(0, Guid.Empty, "", "", false, [component])];
        yield return [component];
        yield return [new ComponentResponse(0, "", "", json, 0, [], [])];
        yield return [binding];
        yield return [action];
        yield return [new ReusableComponentSummary(Guid.Empty, "", false, default)];
        yield return [new ReusableComponentDraftResponse(Guid.Empty, "", "", json, default)];
        yield return [new ReusableComponentResponse(Guid.Empty, "", "", 0, json, default)];
        yield return [new PageContentDraftResponse(Guid.Empty, "", json, default)];
        yield return [new PageContentResponse(Guid.Empty, "", "", 0, json)];
        yield return [new AuthorSessionResponse(Guid.Empty, [], "")];
        yield return [new AuthorPasskeyResponse(Guid.Empty, "", default, null, null)];
    }

    [Theory]
    [MemberData(nameof(ResponseExamples))]
    public async Task RequiredResponsePropertiesMatchActualSerialization(object value)
    {
        await using var factory = new ContractTestApplication();
        using var client = factory.CreateClient();
        var document = JsonNode.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"))!;
        var schema = document["components"]!["schemas"]![value.GetType().Name]!;
        var options = factory.Services
            .GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value;
        var body = JsonSerializer.SerializeToNode(value, value.GetType(), options.SerializerOptions)!
            .AsObject();
        var properties = schema["properties"]!.AsObject();
        var required = schema["required"]!.AsArray()
            .Select(property => property!.GetValue<string>()).Order().ToArray();

        Assert.Equal(properties.Select(property => property.Key).Order(), required);
        Assert.Equal(body.Select(property => property.Key).Order(), required);
        foreach (var (name, property) in properties)
        {
            if (property!["nullable"]?.GetValue<bool>() != true
                && property["type"]?.GetValue<string>() is "string" or "array")
            {
                Assert.NotNull(body[name]);
            }
        }
    }

    [Fact]
    public async Task HealthResponseMatchesDocumentedShape()
    {
        await using var factory = new ContractTestApplication();
        using var client = factory.CreateClient();
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""{"status":"healthy"}"""),
            JsonNode.Parse(await client.GetStringAsync("/health"))));
    }

    [Fact]
    public async Task SwaggerUiIsAvailableInDevelopment()
    {
        await using var factory = new ContractTestApplication();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public async Task SwaggerIsNotServedOutsideDevelopment(string environment)
    {
        await using var factory = new ContractTestApplication();
        await using var nonDevelopmentFactory = factory.WithWebHostBuilder(
            builder => builder.UseEnvironment(environment));
        using var client = nonDevelopmentFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        foreach (var path in new[] { "/swagger", "/swagger/index.html", "/swagger/v1/swagger.json" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task LegacySlugManagementRouteIsRemoved()
    {
        await using var factory = new ContractTestApplication();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/pages/example");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task ServedOpenApiMatchesCheckedInContract()
    {
        await using var factory = new ContractTestApplication();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        var actualJson = await response.Content.ReadAsStringAsync();
        var expectedJson = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Contracts", "openapi.json"));

        var resultsDirectory = Path.Combine(AppContext.BaseDirectory, "TestResults");
        Directory.CreateDirectory(resultsDirectory);
        var generatedPath = Path.Combine(resultsDirectory, "openapi.generated.json");
        await File.WriteAllTextAsync(generatedPath, actualJson);

        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(expectedJson), JsonNode.Parse(actualJson)),
            $"Served OpenAPI differs from api/openapi.json. Review {generatedPath} " +
            "and follow the contract update instructions in README.md.");
    }

    [Fact]
    public async Task OpenApiMarksReaderRoutesAnonymousAndAuthoringRoutesProtected()
    {
        await using var factory = new ContractTestApplication();
        using var client = factory.CreateClient();
        var document = JsonNode.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"))!;
        var paths = document["paths"]!;

        Assert.Null(paths["/api/content/pages"]!["get"]!["security"]);
        Assert.NotNull(paths["/api/content/pages/{pageId}/draft"]!["get"]!["security"]);
        Assert.Null(paths["/api/auth/sign-in/options"]!["post"]!["security"]);
        Assert.NotNull(paths["/api/reusable-components/{componentId}/publish"]!["post"]!["security"]);
        Assert.Contains(
            paths["/api/reusable-components/{componentId}/publish"]!["post"]!["parameters"]!.AsArray(),
            parameter => parameter?["name"]?.GetValue<string>() == "X-CSRF-Token");
    }

    private sealed class ContractTestApplication : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(configuration =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] =
                        "Server=localhost;Database=DiscoverJesusContractTests;Integrated Security=True;TrustServerCertificate=True",
                    ["WebAuthn:RPID"] = "localhost",
                    ["WebAuthn:Origins:0"] = "http://localhost:5173",
                    ["Cors:AllowedOrigins:0"] = "http://localhost:5173"
                }));
            return base.CreateHost(builder);
        }
    }
}
