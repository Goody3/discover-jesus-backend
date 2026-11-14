using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DiscoverJesus.Api.Tests;

public sealed class OpenApiContractTests
{
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

    private sealed class ContractTestApplication : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(configuration =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] =
                        "Server=localhost;Database=DiscoverJesusContractTests;Integrated Security=True;TrustServerCertificate=True"
                }));
            return base.CreateHost(builder);
        }
    }
}
