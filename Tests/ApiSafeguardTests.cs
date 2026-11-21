using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DiscoverJesus.Api.Contracts;
using DiscoverJesus.Api.Data;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DiscoverJesus.Api.Tests;

public sealed class ApiSafeguardTests
{
    private const int MaximumRequestBodySize = 1024 * 1024;
    private const string StudioOrigin = "http://localhost:5173";

    [Fact]
    public async Task CorsDeniesOriginsByDefaultAndAllowsConfiguredOrigins()
    {
        await using var deniedFactory = new SafeguardTestApplication();
        using var deniedClient = deniedFactory.CreateClient();
        using var deniedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/content/pages?locale=en-US");
        deniedRequest.Headers.Add("Origin", StudioOrigin);
        using var deniedResponse = await deniedClient.SendAsync(deniedRequest);
        Assert.Equal(HttpStatusCode.OK, deniedResponse.StatusCode);
        Assert.False(deniedResponse.Headers.Contains("Access-Control-Allow-Origin"));

        await using var allowedFactory = new SafeguardTestApplication(StudioOrigin);
        Assert.Equal(
            StudioOrigin,
            allowedFactory.Services.GetRequiredService<IConfiguration>()["Cors:AllowedOrigins:0"]);
        var corsProvider = allowedFactory.Services.GetRequiredService<ICorsPolicyProvider>();
        var defaultPolicy = await corsProvider.GetPolicyAsync(new DefaultHttpContext(), policyName: null);
        Assert.Contains(StudioOrigin, defaultPolicy!.Origins);
        using var allowedClient = allowedFactory.CreateClient();
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/content/pages?locale=en-US");
        preflight.Headers.Add("Origin", StudioOrigin);
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        using var allowedResponse = await allowedClient.SendAsync(preflight);

        Assert.Equal(HttpStatusCode.NoContent, allowedResponse.StatusCode);
        Assert.Equal(StudioOrigin, Assert.Single(allowedResponse.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task PublishedAndAuthoringRoutesHaveSeparatePerIpRateLimits()
    {
        await using var factory = new SafeguardTestApplication();
        using var client = factory.CreateClient();

        for (var request = 0; request < 120; request++)
        {
            using var response = await client.GetAsync("/api/content/pages?locale=en-US");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var readerLimit = await client.GetAsync("/api/content/pages?locale=en-US");
        Assert.Equal(HttpStatusCode.TooManyRequests, readerLimit.StatusCode);
        using var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        await using var authoringFactory = new SafeguardTestApplication();
        using var authoringClient = authoringFactory.CreateClient();
        for (var request = 0; request < 30; request++)
        {
            using var response = await authoringClient.GetAsync("/api/pages/");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var authoringLimit = await authoringClient.GetAsync("/api/pages/");
        Assert.Equal(HttpStatusCode.TooManyRequests, authoringLimit.StatusCode);
    }

    [Fact]
    public async Task RequestBodyAcceptsOneMiBAndReturns413AboveLimit()
    {
        await using var factory = new SafeguardTestApplication();
        using var client = factory.CreateClient();
        var exactLimitBody = CreatePagePayload(MaximumRequestBodySize);
        Assert.Equal(MaximumRequestBodySize, Encoding.UTF8.GetByteCount(exactLimitBody));

        using var accepted = await client.PostAsync(
            "/api/pages/",
            new StringContent(exactLimitBody, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);

        using var rejected = await client.PostAsync(
            "/api/pages/",
            new StringContent(exactLimitBody + " ", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rejected.StatusCode);
    }

    [Fact]
    public async Task ManagementWritesRejectUnsupportedTypesAndNonObjectPayloadsWithoutPersistence()
    {
        await using var factory = new SafeguardTestApplication();
        using var client = factory.CreateClient();

        using var invalidPageResponse = await client.PostAsJsonAsync(
            "/api/pages/",
            new PageRequest("validation-page", new string('t', 301), false));
        Assert.Equal(HttpStatusCode.BadRequest, invalidPageResponse.StatusCode);

        using var pageBoundaryResponse = await client.PostAsJsonAsync(
            "/api/pages/",
            new PageRequest(new string('s', 200), new string('t', 300), false));
        Assert.Equal(HttpStatusCode.Created, pageBoundaryResponse.StatusCode);
        using var invalidSlugResponse = await client.PostAsJsonAsync(
            "/api/pages/",
            new PageRequest(new string('s', 201), "Too long", false));
        Assert.Equal(HttpStatusCode.BadRequest, invalidSlugResponse.StatusCode);

        using var pageResponse = await client.PostAsJsonAsync(
            "/api/pages/",
            new PageRequest("validation-page", "Validation page", false));
        Assert.Equal(HttpStatusCode.Created, pageResponse.StatusCode);
        var page = await pageResponse.Content.ReadFromJsonAsync<PageSummary>();
        Assert.NotNull(page);

        using var invalidTypeResponse = await client.PostAsJsonAsync(
            $"/api/pages/{page.Id}/components",
            new ComponentRequest("unsupported", "custom-widget", ParseJson("{}"), 0));
        Assert.Equal(HttpStatusCode.BadRequest, invalidTypeResponse.StatusCode);

        using var invalidPropsResponse = await client.PostAsJsonAsync(
            $"/api/pages/{page.Id}/components",
            new ComponentRequest("invalid-props", "text", ParseJson("[]"), 0));
        Assert.Equal(HttpStatusCode.BadRequest, invalidPropsResponse.StatusCode);

        using var componentBoundaryResponse = await client.PostAsJsonAsync(
            $"/api/pages/{page.Id}/components",
            new ComponentRequest(new string('k', 200), "text", ParseJson("{}"), 0));
        Assert.Equal(HttpStatusCode.Created, componentBoundaryResponse.StatusCode);
        using var invalidKeyResponse = await client.PostAsJsonAsync(
            $"/api/pages/{page.Id}/components",
            new ComponentRequest(new string('k', 201), "text", ParseJson("{}"), 1));
        Assert.Equal(HttpStatusCode.BadRequest, invalidKeyResponse.StatusCode);

        using var componentResponse = await client.PostAsJsonAsync(
            $"/api/pages/{page.Id}/components",
            new ComponentRequest("legacy-button", "NBButton", ParseJson("""{"text":"Welcome"}"""), 0));
        Assert.Equal(HttpStatusCode.Created, componentResponse.StatusCode);
        var component = await componentResponse.Content.ReadFromJsonAsync<ComponentResponse>();
        Assert.NotNull(component);

        using var invalidBindingResponse = await client.PostAsJsonAsync(
            $"/api/components/{component.Id}/bindings",
            new BindingRequest("title", "page.title", ParseJson("[]")));
        Assert.Equal(HttpStatusCode.BadRequest, invalidBindingResponse.StatusCode);

        using var bindingResponse = await client.PostAsJsonAsync(
            $"/api/components/{component.Id}/bindings",
            new BindingRequest("title", new string('s', 500), ParseJson("""{"path":"title"}""")));
        Assert.Equal(HttpStatusCode.Created, bindingResponse.StatusCode);
        using var invalidSourceResponse = await client.PostAsJsonAsync(
            $"/api/components/{component.Id}/bindings",
            new BindingRequest("subtitle", new string('s', 501), ParseJson("""{"path":"subtitle"}""")));
        Assert.Equal(HttpStatusCode.BadRequest, invalidSourceResponse.StatusCode);

        using var invalidActionResponse = await client.PostAsJsonAsync(
            $"/api/components/{component.Id}/actions",
            new ActionRequest("tap", "open", ParseJson("null")));
        Assert.Equal(HttpStatusCode.BadRequest, invalidActionResponse.StatusCode);

        using var actionResponse = await client.PostAsJsonAsync(
            $"/api/components/{component.Id}/actions",
            new ActionRequest("tap", "open", ParseJson("""{"target":"welcome"}""")));
        Assert.Equal(HttpStatusCode.Created, actionResponse.StatusCode);

        using var invalidUpdateResponse = await client.PutAsJsonAsync(
            $"/api/components/{component.Id}",
            new ComponentRequest("changed", "custom-widget", ParseJson("{}"), 1));
        Assert.Equal(HttpStatusCode.BadRequest, invalidUpdateResponse.StatusCode);
        var storedComponent = await client.GetFromJsonAsync<ComponentResponse>(
            $"/api/components/{component.Id}");
        Assert.NotNull(storedComponent);
        Assert.Equal("legacy-button", storedComponent.Key);
        Assert.Single(storedComponent.Bindings);
        Assert.Single(storedComponent.Actions);
    }

    private static string CreatePagePayload(int byteLength)
    {
        var payload = """{"slug":"size-limit","title":"Size limit","isPublished":false,"padding":""}""";
        var paddingLength = byteLength - Encoding.UTF8.GetByteCount(payload);
        if (paddingLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength));
        }

        return payload.Insert(payload.Length - 2, new string('x', paddingLength));
    }

    private static JsonElement ParseJson(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    private sealed class SafeguardTestApplication(params string[] allowedOrigins) : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = Guid.NewGuid().ToString();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(configuration =>
            {
                var values = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] =
                        "Server=localhost;Database=DiscoverJesusSafeguardTests;Integrated Security=True;TrustServerCertificate=True"
                };
                for (var index = 0; index < allowedOrigins.Length; index++)
                {
                    values[$"Cors:AllowedOrigins:{index}"] = allowedOrigins[index];
                }

                configuration.AddInMemoryCollection(values);
            });
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<DiscoverJesusDbContext>>();
                services.RemoveAll<DiscoverJesusDbContext>();
                services.AddDbContext<DiscoverJesusDbContext>(
                    options => options.UseInMemoryDatabase(_databaseName));
            });
        }
    }
}
