using System.Text.Json;
using DiscoverJesus.Api.Contracts;
using DiscoverJesus.Api.Data;
using DiscoverJesus.Api.Models;
using DiscoverJesus.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DiscoverJesus.Api.Tests;

public sealed class ReusableContentServiceTests
{
    [Fact]
    public async Task PublishedPageExpandsReferencesAndPreservesProvenance()
    {
        await using var db = CreateDbContext();
        var page = await AddPageAsync(db);
        var service = new ReusableContentService(db);
        var component = await service.CreateComponentAsync(
            new CreateReusableComponentRequest("Welcome card", "en", ParseJson(
                """{"type":"stack","children":[{"type":"text","value":"Welcome"}]}""")),
            CancellationToken.None);

        Assert.True(component.Succeeded);
        var componentId = component.Value!.ComponentId;
        var publishedComponent = await service.PublishComponentAsync(
            componentId,
            "en",
            CancellationToken.None);
        Assert.True(publishedComponent.Succeeded);
        Assert.Equal("Welcome card", publishedComponent.Value!.Name);

        var draft = await service.UpdatePageDraftAsync(
            page.PublicId,
            "en",
            new UpdatePageContentDraftRequest(ParseJson(
                $$$"""[{"type":"componentReference","componentId":"{{{componentId}}}"}]""")),
            CancellationToken.None);
        Assert.True(draft.Succeeded);

        Assert.Equal(409, (await service.DeleteComponentAsync(
            componentId,
            CancellationToken.None)).Error?.StatusCode);
        Assert.Equal(409, (await service.WithdrawComponentAsync(
            componentId,
            CancellationToken.None)).Error?.StatusCode);

        var publishedPage = await service.PublishPageAsync(
            page.PublicId,
            "en",
            CancellationToken.None);
        Assert.True(publishedPage.Succeeded);
        var section = publishedPage.Value!.Sections[0];
        Assert.Equal("stack", section.GetProperty("type").GetString());
        Assert.Equal(componentId.ToString(), section.GetProperty("sourceComponentId").GetString());
        Assert.Equal(
            "Welcome",
            section.GetProperty("children")[0].GetProperty("value").GetString());

        var deleted = await service.DeleteComponentAsync(componentId, CancellationToken.None);
        var withdrawn = await service.WithdrawComponentAsync(componentId, CancellationToken.None);
        Assert.Equal(409, deleted.Error?.StatusCode);
        Assert.Equal(409, withdrawn.Error?.StatusCode);
    }

    [Fact]
    public async Task PageReferenceChildrenReplacePublishedChildren()
    {
        await using var db = CreateDbContext();
        var page = await AddPageAsync(db);
        var service = new ReusableContentService(db);
        var component = await service.CreateComponentAsync(
            new CreateReusableComponentRequest("Card with overrides", "en", ParseJson(
                """{"type":"stack","children":[{"type":"text","value":"Published child"}]}""")),
            CancellationToken.None);
        Assert.True(component.Succeeded);
        var componentId = component.Value!.ComponentId;
        Assert.True((await service.PublishComponentAsync(
            componentId,
            "en",
            CancellationToken.None)).Succeeded);

        var draft = await service.UpdatePageDraftAsync(
            page.PublicId,
            "en",
            new UpdatePageContentDraftRequest(ParseJson(
                $$$"""[{"type":"componentReference","componentId":"{{{componentId}}}","children":[{"type":"text","value":"Override child"}]}]""")),
            CancellationToken.None);
        Assert.True(draft.Succeeded);

        var pageResponse = await service.PublishPageAsync(
            page.PublicId,
            "en",
            CancellationToken.None);

        Assert.True(pageResponse.Succeeded);
        Assert.Equal(
            "Override child",
            pageResponse.Value!.Sections[0].GetProperty("children")[0].GetProperty("value").GetString());
    }

    [Fact]
    public async Task StartingPageDraftFromPublishedCopiesUnexpandedSectionsAndReferences()
    {
        await using var db = CreateDbContext();
        var page = await AddPageAsync(db);
        var service = new ReusableContentService(db);
        var component = await service.CreateComponentAsync(
            new CreateReusableComponentRequest(
                "Referenced component",
                "en",
                ParseJson("""{"type":"text","value":"Published component"}""")),
            CancellationToken.None);
        Assert.True(component.Succeeded);
        Assert.True((await service.PublishComponentAsync(
            component.Value!.ComponentId,
            "en",
            CancellationToken.None)).Succeeded);

        var originalSections = ParseJson(
            $$$"""[{"type":"componentReference","componentId":"{{{component.Value.ComponentId}}}","children":[{"type":"text","value":"Page override"}]}]""");
        Assert.True((await service.UpdatePageDraftAsync(
            page.PublicId,
            "en",
            new UpdatePageContentDraftRequest(originalSections),
            CancellationToken.None)).Succeeded);
        Assert.True((await service.PublishPageAsync(
            page.PublicId,
            "en",
            CancellationToken.None)).Succeeded);

        var started = await service.StartPageDraftFromPublishedAsync(
            page.PublicId,
            "en",
            CancellationToken.None);

        Assert.True(started.Succeeded);
        Assert.Equal("componentReference", started.Value!.Sections[0].GetProperty("type").GetString());
        Assert.Equal(
            component.Value.ComponentId.ToString(),
            started.Value.Sections[0].GetProperty("componentId").GetString());
        Assert.Equal(
            "Page override",
            started.Value.Sections[0].GetProperty("children")[0].GetProperty("value").GetString());
        Assert.Equal(2, await db.PageComponentReferences.CountAsync(
            item => item.ComponentPublicId == component.Value.ComponentId));

        var conflict = await service.StartPageDraftFromPublishedAsync(
            page.PublicId,
            "en",
            CancellationToken.None);
        Assert.Equal(409, conflict.Error?.StatusCode);

        var pageWithoutPublishedContent = await AddPageAsync(db);
        var notFound = await service.StartPageDraftFromPublishedAsync(
            pageWithoutPublishedContent.PublicId,
            "en",
            CancellationToken.None);
        Assert.Equal(404, notFound.Error?.StatusCode);
    }

    [Fact]
    public async Task PagePublicationRejectsUnresolvedComponentReferences()
    {
        await using var db = CreateDbContext();
        var page = await AddPageAsync(db);
        var service = new ReusableContentService(db);
        var missingComponentId = Guid.NewGuid();

        var draft = await service.UpdatePageDraftAsync(
            page.PublicId,
            "en",
            new UpdatePageContentDraftRequest(ParseJson(
                $$$"""[{"type":"componentReference","componentId":"{{{missingComponentId}}}"}]""")),
            CancellationToken.None);
        Assert.True(draft.Succeeded);

        var published = await service.PublishPageAsync(
            page.PublicId,
            "en",
            CancellationToken.None);
        Assert.Equal(422, published.Error?.StatusCode);
        Assert.False((await db.Pages.SingleAsync(item => item.Id == page.Id)).IsPublished);
    }

    [Fact]
    public async Task ComponentReadDoesNotFallBackToAnotherLocale()
    {
        await using var db = CreateDbContext();
        var service = new ReusableContentService(db);
        var component = await service.CreateComponentAsync(
            new CreateReusableComponentRequest("Greeting", "en", ParseJson("""{"type":"text","value":"Hello"}""")),
            CancellationToken.None);
        Assert.True(component.Succeeded);

        var published = await service.PublishComponentAsync(
            component.Value!.ComponentId,
            "en",
            CancellationToken.None);
        Assert.True(published.Succeeded);

        var french = await service.GetComponentAsync(
            component.Value.ComponentId,
            "fr",
            CancellationToken.None);
        Assert.Equal(404, french.Error?.StatusCode);
    }

    [Fact]
    public async Task ComponentRevisionsRemainImmutableAndCanBeWithdrawnWhenUnreferenced()
    {
        await using var db = CreateDbContext();
        var service = new ReusableContentService(db);
        var component = await service.CreateComponentAsync(
            new CreateReusableComponentRequest("Revision sample", "en", ParseJson("""{"type":"text","value":"First"}""")),
            CancellationToken.None);
        Assert.True(component.Succeeded);
        var componentId = component.Value!.ComponentId;

        var firstDraft = await service.GetComponentDraftAsync(
            componentId,
            "en",
            CancellationToken.None);
        Assert.True(firstDraft.Succeeded);
        var firstPublish = await service.PublishComponentAsync(
            componentId,
            "en",
            CancellationToken.None);
        Assert.Equal(1, firstPublish.Value?.Revision);

        var update = await service.UpdateComponentDraftAsync(
            componentId,
            "en",
            new UpdateReusableComponentDraftRequest(ParseJson(
                """{"type":"text","value":"Second"}""")),
            CancellationToken.None);
        Assert.True(update.Succeeded);
        var publishedBeforeSecondPublish = await service.GetComponentAsync(
            componentId,
            "en",
            CancellationToken.None);
        Assert.Equal("First", publishedBeforeSecondPublish.Value!.Content.GetProperty("value").GetString());

        var secondPublish = await service.PublishComponentAsync(
            componentId,
            "en",
            CancellationToken.None);
        Assert.Equal(2, secondPublish.Value?.Revision);
        Assert.Equal("Second", secondPublish.Value!.Content.GetProperty("value").GetString());

        Assert.True((await service.WithdrawComponentAsync(
            componentId,
            CancellationToken.None)).Succeeded);
        Assert.Equal(404, (await service.GetComponentAsync(
            componentId,
            "en",
            CancellationToken.None)).Error?.StatusCode);
        Assert.True((await service.DeleteComponentAsync(
            componentId,
            CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task PageDraftRejectsUnavailableNavigationDestinations()
    {
        await using var db = CreateDbContext();
        var page = await AddPageAsync(db);
        var destination = await AddPageAsync(db, isPublished: false);
        var service = new ReusableContentService(db);

        var draft = await service.UpdatePageDraftAsync(
            page.PublicId,
            "en",
            new UpdatePageContentDraftRequest(ParseJson(
                $$$"""[{"type":"button","action":{"type":"navigate","pageId":"{{{destination.PublicId}}}"}}]""")),
            CancellationToken.None);
        Assert.Equal(422, draft.Error?.StatusCode);
    }

    [Fact]
    public async Task PageDraftRejectsUnpublishedSelfNavigation()
    {
        await using var db = CreateDbContext();
        var page = await AddPageAsync(db, isPublished: false);
        var service = new ReusableContentService(db);

        var draft = await service.UpdatePageDraftAsync(
            page.PublicId,
            "en",
            new UpdatePageContentDraftRequest(ParseJson(
                $$$"""[{"type":"button","action":{"type":"navigate","pageId":"{{{page.PublicId}}}"}}]""")),
            CancellationToken.None);
        Assert.Equal(422, draft.Error?.StatusCode);
    }

    [Fact]
    public async Task PageReadRechecksNavigationDestinationsAtRuntime()
    {
        await using var db = CreateDbContext();
        var page = await AddPageAsync(db);
        var destination = await AddPageAsync(db, isPublished: true, hasEnglishRevision: true);
        var service = new ReusableContentService(db);

        var draft = await service.UpdatePageDraftAsync(
            page.PublicId,
            "en",
            new UpdatePageContentDraftRequest(ParseJson(
                $$$"""[{"type":"button","action":{"type":"navigate","pageId":"{{{destination.PublicId}}}"}}]""")),
            CancellationToken.None);
        Assert.True(draft.Succeeded);
        Assert.True((await service.PublishPageAsync(
            page.PublicId,
            "en",
            CancellationToken.None)).Succeeded);

        destination.IsPublished = false;
        await db.SaveChangesAsync();

        var delivered = await service.GetPageAsync(page.PublicId, "en", CancellationToken.None);
        Assert.Equal(422, delivered.Error?.StatusCode);
    }

    [Fact]
    public async Task DraftValidationRejectsInvalidNavigationIdentifiers()
    {
        await using var db = CreateDbContext();
        var page = await AddPageAsync(db);
        var service = new ReusableContentService(db);

        var result = await service.UpdatePageDraftAsync(
            page.PublicId,
            "en",
            new UpdatePageContentDraftRequest(ParseJson(
                """[{"type":"button","action":{"type":"navigate","pageId":"not-a-uuid"}}]""")),
            CancellationToken.None);

        Assert.Equal(400, result.Error?.StatusCode);
        Assert.Contains("pageId", string.Join(" ", result.Error!.ValidationErrors!["sections"]));
    }

    [Fact]
    public async Task DraftValidationRejectsLegacyTargetAndRequiresNavigateIfCondition()
    {
        await using var db = CreateDbContext();
        var page = await AddPageAsync(db);
        var service = new ReusableContentService(db);
        var legacyTarget = await service.UpdatePageDraftAsync(
            page.PublicId,
            "en",
            new UpdatePageContentDraftRequest(ParseJson(
                """[{"type":"button","action":{"type":"navigate","target":"somewhere"}}]""")),
            CancellationToken.None);
        var missingCondition = await service.UpdatePageDraftAsync(
            page.PublicId,
            "en",
            new UpdatePageContentDraftRequest(ParseJson(
                """[{"type":"button","action":{"type":"navigateIf","pageId":"00000000-0000-0000-0000-000000000001"}}]""")),
            CancellationToken.None);

        Assert.Equal(400, legacyTarget.Error?.StatusCode);
        Assert.Equal(400, missingCondition.Error?.StatusCode);
        Assert.Contains("target", string.Join(" ", legacyTarget.Error!.ValidationErrors!["sections"]));
        Assert.Contains("condition", string.Join(" ", missingCondition.Error!.ValidationErrors!["sections"]));
    }

    [Fact]
    public async Task DraftValidationAcceptsConditionOnActionTypesAllowedByTheSchema()
    {
        await using var db = CreateDbContext();
        var page = await AddPageAsync(db);
        var service = new ReusableContentService(db);

        var result = await service.UpdatePageDraftAsync(
            page.PublicId,
            "en",
            new UpdatePageContentDraftRequest(ParseJson(
                """[{"type":"button","action":{"type":"showModal","modalId":"lesson","condition":"visible"}},{"type":"button","action":{"type":"openExternalUrl","url":"https://example.org","condition":"visible"}}]""")),
            CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ComponentDraftRejectsNonHttpsExternalUrl()
    {
        await using var db = CreateDbContext();
        var service = new ReusableContentService(db);
        var component = await service.CreateComponentAsync(
            new CreateReusableComponentRequest("Unsafe link", "en", ParseJson(
                """{"type":"button","action":{"type":"openExternalUrl","url":"http://example.org"}}""")),
            CancellationToken.None);

        Assert.Equal(400, component.Error?.StatusCode);
        Assert.Contains("HTTPS", string.Join(" ", component.Error!.ValidationErrors!["content"]));
    }

    [Fact]
    public async Task ComponentDraftRejectsUnavailableNavigationDestination()
    {
        await using var db = CreateDbContext();
        var destination = await AddPageAsync(db, isPublished: false);
        var service = new ReusableContentService(db);

        var component = await service.CreateComponentAsync(
            new CreateReusableComponentRequest("Navigation sample", "en", ParseJson(
                $$$"""{"type":"button","action":{"type":"navigate","pageId":"{{{destination.PublicId}}}"}}""")),
            CancellationToken.None);

        Assert.Equal(422, component.Error?.StatusCode);
    }

    [Fact]
    public async Task ComponentNamesAreGloballyUniqueAndListedByFriendlyName()
    {
        await using var db = CreateDbContext();
        var service = new ReusableContentService(db);
        var first = await service.CreateComponentAsync(
            new CreateReusableComponentRequest(
                " Welcome card ",
                "en",
                ParseJson("""{"type":"text","value":"Welcome"}""")),
            CancellationToken.None);
        var second = await service.CreateComponentAsync(
            new CreateReusableComponentRequest(
                "Footer",
                "en",
                ParseJson("""{"type":"text","value":"Footer"}""")),
            CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.Equal("Welcome card", first.Value!.Name);
        Assert.True(second.Succeeded);
        Assert.Equal(409, (await service.CreateComponentAsync(
            new CreateReusableComponentRequest(
                "welcome CARD",
                "fr",
                ParseJson("""{"type":"text","value":"Bienvenue"}""")),
            CancellationToken.None)).Error?.StatusCode);
        Assert.Equal(409, (await service.UpdateComponentNameAsync(
            second.Value!.ComponentId,
            new UpdateReusableComponentNameRequest("WELCOME CARD"),
            CancellationToken.None)).Error?.StatusCode);

        var renamed = await service.UpdateComponentNameAsync(
            first.Value.ComponentId,
            new UpdateReusableComponentNameRequest("Welcome panel"),
            CancellationToken.None);
        Assert.True(renamed.Succeeded);
        Assert.Equal("Welcome panel", renamed.Value!.Name);

        var components = await service.GetComponentsAsync(CancellationToken.None);
        Assert.Equal(["Footer", "Welcome panel"], components.Select(item => item.Name));
        Assert.DoesNotContain(components, item => item.Name.Contains(first.Value.ComponentId.ToString()));

        Assert.True((await service.WithdrawComponentAsync(
            first.Value.ComponentId,
            CancellationToken.None)).Succeeded);
        Assert.Equal(409, (await service.CreateComponentAsync(
            new CreateReusableComponentRequest(
                "welcome panel",
                "en",
                ParseJson("""{"type":"text","value":"Duplicate"}""")),
            CancellationToken.None)).Error?.StatusCode);
        Assert.Contains(
            await service.GetComponentsAsync(CancellationToken.None),
            item => item.ComponentId == first.Value.ComponentId && item.IsWithdrawn);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ComponentNameRejectsEmptyOrWhitespaceValues(string name)
    {
        await using var db = CreateDbContext();
        var service = new ReusableContentService(db);

        var result = await service.CreateComponentAsync(
            new CreateReusableComponentRequest(
                name,
                "en",
                ParseJson("""{"type":"text","value":"Example"}""")),
            CancellationToken.None);

        Assert.Equal(400, result.Error?.StatusCode);
    }

    [Fact]
    public async Task ComponentNameAllows200CharactersAndRejectsLongerNames()
    {
        await using var db = CreateDbContext();
        var service = new ReusableContentService(db);

        var allowed = await service.CreateComponentAsync(
            new CreateReusableComponentRequest(
                new string('a', 200),
                "en",
                ParseJson("""{"type":"text","value":"Allowed"}""")),
            CancellationToken.None);
        var rejected = await service.CreateComponentAsync(
            new CreateReusableComponentRequest(
                new string('b', 201),
                "en",
                ParseJson("""{"type":"text","value":"Rejected"}""")),
            CancellationToken.None);

        Assert.True(allowed.Succeeded);
        Assert.Equal(200, allowed.Value!.Name.Length);
        Assert.Equal(400, rejected.Error?.StatusCode);
    }

    private static DiscoverJesusDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<DiscoverJesusDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings =>
                warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new DiscoverJesusDbContext(options);
    }

    private static async Task<Page> AddPageAsync(
        DiscoverJesusDbContext db,
        bool isPublished = false,
        bool hasEnglishRevision = false)
    {
        var page = new Page
        {
            Slug = Guid.NewGuid().ToString("N"),
            Title = "Test page",
            IsPublished = isPublished
        };
        db.Pages.Add(page);
        await db.SaveChangesAsync();

        if (hasEnglishRevision)
        {
            db.PageContentRevisions.Add(new PageContentRevision
            {
                PageId = page.Id,
                Locale = "en",
                RevisionNumber = 1,
                SectionsJson = "[]",
                PublishedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        return page;
    }

    private static JsonElement ParseJson(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }
}
