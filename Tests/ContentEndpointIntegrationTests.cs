using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DiscoverJesus.Api.Contracts;
using Xunit;

namespace DiscoverJesus.Api.Tests;

[CollectionDefinition("SQL Server integration", DisableParallelization = true)]
public sealed class SqlServerIntegrationCollection
{
}

[Collection("SQL Server integration")]
public sealed class ContentEndpointIntegrationTests
{
    [Fact]
    public async Task PagesSupportCrudValidationConflictsAndNotFoundResponses()
    {
        await using var app = await SqlServerIntegrationTestApplication.StartAsync();

        using var invalidPageResponse = await app.Client.PostAsJsonAsync(
            "/api/pages",
            new PageRequest("invalid slug", "Invalid", false));
        Assert.Equal(HttpStatusCode.BadRequest, invalidPageResponse.StatusCode);

        var createResponse = await app.Client.PostAsJsonAsync(
            "/api/pages",
            new PageRequest("welcome", "Welcome", true));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createdPage = await createResponse.Content.ReadFromJsonAsync<PageSummary>();
        Assert.NotNull(createdPage);
        Assert.NotEqual(Guid.Empty, createdPage.PublicId);

        using var duplicatePageResponse = await app.Client.PostAsJsonAsync(
            "/api/pages",
            new PageRequest("welcome", "Duplicate", false));
        Assert.Equal(HttpStatusCode.Conflict, duplicatePageResponse.StatusCode);

        var pages = await app.Client.GetFromJsonAsync<PageSummary[]>("/api/pages");
        Assert.Contains(pages!, page => page.Id == createdPage.Id);

        var pageResponse = await app.Client.GetFromJsonAsync<PageResponse>("/api/pages/welcome");
        Assert.NotNull(pageResponse);
        Assert.Equal(createdPage.Id, pageResponse.Id);

        var updateResponse = await app.Client.PutAsJsonAsync(
            $"/api/pages/{createdPage.Id}",
            new PageRequest("welcome-home", "Welcome home", false));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        using var missingUpdateResponse = await app.Client.PutAsJsonAsync(
            "/api/pages/99999",
            new PageRequest("missing", "Missing", false));
        Assert.Equal(HttpStatusCode.NotFound, missingUpdateResponse.StatusCode);

        using var missingPageResponse = await app.Client.GetAsync("/api/pages/not-found");
        Assert.Equal(HttpStatusCode.NotFound, missingPageResponse.StatusCode);

        using var deleteResponse = await app.Client.DeleteAsync($"/api/pages/{createdPage.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var deletedPageResponse = await app.Client.GetAsync("/api/pages/welcome-home");
        Assert.Equal(HttpStatusCode.NotFound, deletedPageResponse.StatusCode);
    }

    [Fact]
    public async Task ComponentsBindingsAndActionsSupportCrudValidationAndNotFoundResponses()
    {
        await using var app = await SqlServerIntegrationTestApplication.StartAsync();
        var page = await CreatePageAsync(app.Client);

        using var missingComponentsResponse = await app.Client.GetAsync("/api/pages/99999/components");
        Assert.Equal(HttpStatusCode.NotFound, missingComponentsResponse.StatusCode);

        using var invalidComponentResponse = await app.Client.PostAsJsonAsync(
            $"/api/pages/{page.Id}/components",
            new ComponentRequest("", "text", ParseJson("""{"text":"invalid"}"""), 0));
        Assert.Equal(HttpStatusCode.BadRequest, invalidComponentResponse.StatusCode);

        var componentResponse = await app.Client.PostAsJsonAsync(
            $"/api/pages/{page.Id}/components",
            new ComponentRequest("hero", "text", ParseJson("""{"text":"Welcome"}"""), 0));
        Assert.Equal(HttpStatusCode.Created, componentResponse.StatusCode);
        var component = await componentResponse.Content.ReadFromJsonAsync<ComponentResponse>();
        Assert.NotNull(component);

        var components = await app.Client.GetFromJsonAsync<ComponentResponse[]>(
            $"/api/pages/{page.Id}/components");
        Assert.Contains(components!, item => item.Id == component.Id);

        var componentRead = await app.Client.GetFromJsonAsync<ComponentResponse>(
            $"/api/components/{component.Id}");
        Assert.NotNull(componentRead);
        Assert.Equal(component.Id, componentRead.Id);

        using var missingComponentResponse = await app.Client.GetAsync("/api/components/99999");
        Assert.Equal(HttpStatusCode.NotFound, missingComponentResponse.StatusCode);

        var updateComponentResponse = await app.Client.PutAsJsonAsync(
            $"/api/components/{component.Id}",
            new ComponentRequest("hero-updated", "text", ParseJson("""{"text":"Updated"}"""), 1));
        Assert.Equal(HttpStatusCode.OK, updateComponentResponse.StatusCode);

        using var missingBindingsResponse = await app.Client.GetAsync(
            "/api/components/99999/bindings");
        Assert.Equal(HttpStatusCode.NotFound, missingBindingsResponse.StatusCode);

        using var invalidBindingResponse = await app.Client.PostAsJsonAsync(
            $"/api/components/{component.Id}/bindings",
            new BindingRequest("title", "", ParseJson("""{"path":"title"}""")));
        Assert.Equal(HttpStatusCode.BadRequest, invalidBindingResponse.StatusCode);

        var bindingResponse = await app.Client.PostAsJsonAsync(
            $"/api/components/{component.Id}/bindings",
            new BindingRequest("title", "page.title", ParseJson("""{"path":"title"}""")));
        Assert.Equal(HttpStatusCode.Created, bindingResponse.StatusCode);
        var binding = await bindingResponse.Content.ReadFromJsonAsync<BindingResponse>();
        Assert.NotNull(binding);

        var bindings = await app.Client.GetFromJsonAsync<BindingResponse[]>(
            $"/api/components/{component.Id}/bindings");
        Assert.Contains(bindings!, item => item.Id == binding.Id);
        Assert.Equal(HttpStatusCode.OK, (await app.Client.GetAsync($"/api/bindings/{binding.Id}")).StatusCode);
        using var updateBindingResponse = await app.Client.PutAsJsonAsync(
            $"/api/bindings/{binding.Id}",
            new BindingRequest("title", "page.heading", ParseJson("""{"path":"heading"}""")));
        Assert.Equal(HttpStatusCode.OK, updateBindingResponse.StatusCode);
        using var missingBindingResponse = await app.Client.GetAsync("/api/bindings/99999");
        Assert.Equal(HttpStatusCode.NotFound, missingBindingResponse.StatusCode);
        using var deleteBindingResponse = await app.Client.DeleteAsync($"/api/bindings/{binding.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteBindingResponse.StatusCode);

        using var missingActionsResponse = await app.Client.GetAsync(
            $"/api/components/99999/actions");
        Assert.Equal(HttpStatusCode.NotFound, missingActionsResponse.StatusCode);

        using var invalidActionResponse = await app.Client.PostAsJsonAsync(
            $"/api/components/{component.Id}/actions",
            new ActionRequest("tap", "", ParseJson("{}")));
        Assert.Equal(HttpStatusCode.BadRequest, invalidActionResponse.StatusCode);

        var actionResponse = await app.Client.PostAsJsonAsync(
            $"/api/components/{component.Id}/actions",
            new ActionRequest("tap", "navigate", ParseJson("""{"target":"home"}""")));
        Assert.Equal(HttpStatusCode.Created, actionResponse.StatusCode);
        var action = await actionResponse.Content.ReadFromJsonAsync<ActionResponse>();
        Assert.NotNull(action);

        var actions = await app.Client.GetFromJsonAsync<ActionResponse[]>(
            $"/api/components/{component.Id}/actions");
        Assert.Contains(actions!, item => item.Id == action.Id);
        Assert.Equal(HttpStatusCode.OK, (await app.Client.GetAsync($"/api/actions/{action.Id}")).StatusCode);
        using var updateActionResponse = await app.Client.PutAsJsonAsync(
            $"/api/actions/{action.Id}",
            new ActionRequest("tap", "open", ParseJson("""{"target":"welcome"}""")));
        Assert.Equal(HttpStatusCode.OK, updateActionResponse.StatusCode);
        using var missingActionResponse = await app.Client.GetAsync("/api/actions/99999");
        Assert.Equal(HttpStatusCode.NotFound, missingActionResponse.StatusCode);
        using var deleteActionResponse = await app.Client.DeleteAsync($"/api/actions/{action.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteActionResponse.StatusCode);

        using var deleteComponentResponse = await app.Client.DeleteAsync(
            $"/api/components/{component.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteComponentResponse.StatusCode);
        using var deletedComponentResponse = await app.Client.GetAsync(
            $"/api/components/{component.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deletedComponentResponse.StatusCode);
    }

    [Fact]
    public async Task ReusableComponentEndpointsSupportDraftPublishAndLocaleIsolation()
    {
        await using var app = await SqlServerIntegrationTestApplication.StartAsync();

        using var invalidCreateResponse = await app.Client.PostAsJsonAsync(
            "/api/reusable-components",
            new CreateReusableComponentRequest("", "en", ParseJson("""{"type":"text","value":"x"}""")));
        Assert.Equal(HttpStatusCode.BadRequest, invalidCreateResponse.StatusCode);

        var createResponse = await app.Client.PostAsJsonAsync(
            "/api/reusable-components",
            new CreateReusableComponentRequest(
                "Integration Component",
                "en",
                ParseJson("""{"type":"text","value":"Draft"}""")));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var draft = await createResponse.Content.ReadFromJsonAsync<ReusableComponentDraftResponse>();
        Assert.NotNull(draft);

        var componentDraft = await app.Client.GetFromJsonAsync<ReusableComponentDraftResponse>(
            $"/api/reusable-components/{draft.ComponentId}/draft?locale=en");
        Assert.NotNull(componentDraft);
        Assert.Equal("en", componentDraft.Locale);

        using var updateDraftResponse = await app.Client.PutAsJsonAsync(
            $"/api/reusable-components/{draft.ComponentId}/draft?locale=en",
            new UpdateReusableComponentDraftRequest(
                ParseJson("""{"type":"text","value":"Updated"}""")));
        Assert.Equal(HttpStatusCode.OK, updateDraftResponse.StatusCode);

        var publishResponse = await app.Client.PostAsync(
            $"/api/reusable-components/{draft.ComponentId}/publish?locale=en",
            content: null);
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
        var publishedComponent =
            await publishResponse.Content.ReadFromJsonAsync<ReusableComponentResponse>();
        Assert.NotNull(publishedComponent);
        Assert.Equal(1, publishedComponent.Revision);

        var publishedRead = await app.Client.GetFromJsonAsync<ReusableComponentResponse>(
            $"/api/reusable-components/{draft.ComponentId}?locale=en");
        Assert.NotNull(publishedRead);
        Assert.Equal("Updated", publishedRead.Content.GetProperty("value").GetString());

        using var missingLocaleResponse = await app.Client.GetAsync(
            $"/api/reusable-components/{draft.ComponentId}?locale=fr");
        Assert.Equal(HttpStatusCode.NotFound, missingLocaleResponse.StatusCode);
        using var missingDraftResponse = await app.Client.GetAsync(
            $"/api/reusable-components/{Guid.NewGuid()}/draft?locale=en");
        Assert.Equal(HttpStatusCode.NotFound, missingDraftResponse.StatusCode);

        var components = await app.Client.GetFromJsonAsync<ReusableComponentSummary[]>(
            "/api/reusable-components");
        Assert.Contains(components!, item => item.ComponentId == draft.ComponentId);
    }

    [Fact]
    public async Task PublishedPageContentSupportsDraftPublishAndLocaleIsolation()
    {
        await using var app = await SqlServerIntegrationTestApplication.StartAsync();
        var page = await CreatePageAsync(app.Client);

        using var invalidDraftResponse = await app.Client.PutAsJsonAsync(
            $"/api/content/pages/{page.PublicId}/draft?locale=en",
            new UpdatePageContentDraftRequest(ParseJson("""{"not":"an array"}""")));
        Assert.Equal(HttpStatusCode.BadRequest, invalidDraftResponse.StatusCode);

        using var saveDraftResponse = await app.Client.PutAsJsonAsync(
            $"/api/content/pages/{page.PublicId}/draft?locale=en",
            new UpdatePageContentDraftRequest(ParseJson("""[]""")));
        Assert.Equal(HttpStatusCode.OK, saveDraftResponse.StatusCode);

        var pageDraft = await app.Client.GetFromJsonAsync<PageContentDraftResponse>(
            $"/api/content/pages/{page.PublicId}/draft?locale=en");
        Assert.NotNull(pageDraft);
        Assert.Equal("en", pageDraft.Locale);

        using var publishResponse = await app.Client.PostAsync(
            $"/api/content/pages/{page.PublicId}/publish?locale=en",
            content: null);
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
        var published = await publishResponse.Content.ReadFromJsonAsync<PageContentResponse>();
        Assert.NotNull(published);
        Assert.Equal(page.PublicId, published.Id);
        Assert.Equal(1, published.Version);

        var publishedRead = await app.Client.GetFromJsonAsync<PageContentResponse>(
            $"/api/content/pages/{page.PublicId}?locale=en");
        Assert.NotNull(publishedRead);
        Assert.Equal(1, publishedRead.Version);

        using var missingLocaleResponse = await app.Client.GetAsync(
            $"/api/content/pages/{page.PublicId}?locale=fr");
        Assert.Equal(HttpStatusCode.NotFound, missingLocaleResponse.StatusCode);

        using var startDraftResponse = await app.Client.PostAsync(
            $"/api/content/pages/{page.PublicId}/draft/from-published?locale=en",
            content: null);
        Assert.Equal(HttpStatusCode.OK, startDraftResponse.StatusCode);

        using var missingPageResponse = await app.Client.GetAsync(
            $"/api/content/pages/{Guid.NewGuid()}?locale=en");
        Assert.Equal(HttpStatusCode.NotFound, missingPageResponse.StatusCode);
    }

    private static async Task<PageSummary> CreatePageAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/pages",
            new PageRequest($"integration-{Guid.NewGuid():N}", "Integration page", true));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PageSummary>())!;
    }

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
