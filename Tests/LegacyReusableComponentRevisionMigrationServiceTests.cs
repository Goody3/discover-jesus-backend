using System.Text.Json;
using DiscoverJesus.Api.Contracts;
using DiscoverJesus.Api.Data;
using DiscoverJesus.Api.Models;
using DiscoverJesus.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DiscoverJesus.Api.Tests;

public sealed class LegacyReusableComponentRevisionMigrationServiceTests
{
    private const string MigrationVersion = "epic003-v1";
    private static readonly Guid MigrationBatchId =
        Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid ComponentPublicId =
        Guid.Parse("40000000-0000-0000-0000-000000000004");
    private static readonly Guid PagePublicId =
        Guid.Parse("10000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task AppliesApprovedPayloadsAndPreservesOriginalRevisions()
    {
        var options = CreateOptions();
        await SeedAsync(options);
        var originalPayloads = new[] { Payload("First"), Payload("Second") };

        await using (var db = new DiscoverJesusDbContext(options))
        {
            var result = await CreateService(db).ApplyAsync(
                MigrationVersion,
                MigrationBatchId,
                expectedCounts: ExpectedCounts(),
                CancellationToken.None);

            Assert.Equal(2, result.SourceRevisionCount);
            Assert.Equal(2, result.CreatedRevisionCount);
            Assert.Equal(0, result.ExistingRevisionCount);
            Assert.Equal(2, result.MappingReport.ReadyCount);
            Assert.Equal(0, result.MappingReport.BlockedCount);

            var revisions = await db.ReusableComponentRevisions
                .OrderBy(revision => revision.RevisionNumber)
                .ToListAsync();
            Assert.Equal(new[] { 1, 2, 3, 4 }, revisions.Select(revision => revision.RevisionNumber));
            Assert.Equal(originalPayloads, revisions.Take(2).Select(revision => revision.ContentJson));

            using var migratedPayload = JsonDocument.Parse(revisions[3].ContentJson);
            Assert.Equal("text", migratedPayload.RootElement.GetProperty("type").GetString());
            Assert.Equal(
                "headline",
                migratedPayload.RootElement.GetProperty("legacySource").GetProperty("key").GetString());
            Assert.Equal(result.MigratedAt, revisions[3].PublishedAt);

            var contentService = new ReusableContentService(db);
            var currentComponent = await contentService.GetComponentAsync(
                ComponentPublicId,
                "en",
                CancellationToken.None);
            Assert.True(currentComponent.Succeeded);
            Assert.Equal(4, currentComponent.Value!.Revision);
            Assert.Equal("Second", currentComponent.Value.Content.GetProperty("value").GetString());

            var page = await db.Pages.SingleAsync(item => item.PublicId == PagePublicId);
            using var pageSections = JsonDocument.Parse(
                $$"""[{"type":"componentReference","componentId":"{{ComponentPublicId}}"}]""");
            var pageDraft = await contentService.UpdatePageDraftAsync(
                page.PublicId,
                "en",
                new UpdatePageContentDraftRequest(pageSections.RootElement.Clone()),
                CancellationToken.None);
            Assert.True(pageDraft.Succeeded);
            var publishedPage = await contentService.PublishPageAsync(
                page.PublicId,
                "en",
                CancellationToken.None);
            Assert.True(publishedPage.Succeeded);
            Assert.Equal(
                "Second",
                publishedPage.Value!.Sections[0].GetProperty("value").GetString());

            var lineages = await db.ReusableComponentRevisionLineages
                .OrderBy(lineage => lineage.SourceRevisionId)
                .ToListAsync();
            Assert.Equal(2, lineages.Count);
            Assert.All(lineages, lineage =>
            {
                Assert.Equal(MigrationVersion, lineage.MigrationVersion);
                Assert.Equal(MigrationBatchId, lineage.MigrationBatchId);
                Assert.Equal(result.MigratedAt, lineage.MigratedAt);
            });
        }

        await using (var db = new DiscoverJesusDbContext(options))
        {
            var retry = await CreateService(db).ApplyAsync(
                MigrationVersion,
                Guid.NewGuid(),
                expectedCounts: ExpectedCounts(),
                CancellationToken.None);

            Assert.Equal(0, retry.CreatedRevisionCount);
            Assert.Equal(2, retry.ExistingRevisionCount);
            Assert.Equal(4, await db.ReusableComponentRevisions.CountAsync());
            Assert.Equal(2, await db.ReusableComponentRevisionLineages.CountAsync());
            var existingLineage = await db.ReusableComponentRevisionLineages.FirstAsync();
            Assert.Equal(existingLineage.MigrationBatchId, retry.MigrationBatchId);
            Assert.Equal(existingLineage.MigratedAt, retry.MigratedAt);
            Assert.Equal(originalPayloads, await db.ReusableComponentRevisions
                .Where(revision => revision.RevisionNumber <= 2)
                .OrderBy(revision => revision.RevisionNumber)
                .Select(revision => revision.ContentJson)
                .ToArrayAsync());

            var contentService = new ReusableContentService(db);
            using var draftContent = JsonDocument.Parse("""{"type":"text","value":"Future"}""");
            var updatedDraft = await contentService.UpdateComponentDraftAsync(
                ComponentPublicId,
                "en",
                new UpdateReusableComponentDraftRequest(draftContent.RootElement),
                CancellationToken.None);
            Assert.True(updatedDraft.Succeeded);

            var publishedComponent = await contentService.PublishComponentAsync(
                ComponentPublicId,
                "en",
                CancellationToken.None);
            Assert.True(publishedComponent.Succeeded);
            Assert.Equal(5, publishedComponent.Value!.Revision);
            Assert.Equal(
                "Future",
                publishedComponent.Value.Content.GetProperty("value").GetString());
        }
    }

    [Fact]
    public async Task RefusesToRepairPartialExistingLineage()
    {
        var options = CreateOptions();
        await SeedAsync(options);
        await using (var db = new DiscoverJesusDbContext(options))
        {
            await CreateService(db).ApplyAsync(
                MigrationVersion,
                MigrationBatchId,
                expectedCounts: ExpectedCounts(),
                CancellationToken.None);
        }

        await using (var db = new DiscoverJesusDbContext(options))
        {
            var lineageToRemove = await db.ReusableComponentRevisionLineages
                .OrderBy(lineage => lineage.SourceRevisionId)
                .LastAsync();
            var migratedRevision = await db.ReusableComponentRevisions
                .SingleAsync(revision => revision.Id == lineageToRemove.MigratedRevisionId);
            db.Remove(lineageToRemove);
            db.Remove(migratedRevision);
            await db.SaveChangesAsync();
        }

        await using (var db = new DiscoverJesusDbContext(options))
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                CreateService(db).ApplyAsync(
                    MigrationVersion,
                    Guid.NewGuid(),
                    expectedCounts: ExpectedCounts(),
                    CancellationToken.None));

            Assert.Contains("partial migrations are not safe to repair", error.Message);
            Assert.Equal(3, await db.ReusableComponentRevisions.CountAsync());
            Assert.Single(await db.ReusableComponentRevisionLineages.ToListAsync());
        }
    }

    [Fact]
    public async Task RejectsChangedSourceInventoryBeforeWriting()
    {
        var options = CreateOptions();
        await SeedAsync(options);

        await using var db = new DiscoverJesusDbContext(options);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(db).ApplyAsync(
                MigrationVersion,
                MigrationBatchId,
                expectedCounts: ExpectedCounts(sourceRevisionCount: 3),
                CancellationToken.None));

        Assert.Contains("Expected 3 source revisions but found 2", error.Message);
        Assert.Equal(2, await db.ReusableComponentRevisions.CountAsync());
        Assert.Empty(await db.ReusableComponentRevisionLineages.ToListAsync());
    }

    private static LegacyReusableComponentRevisionMigrationService CreateService(
        DiscoverJesusDbContext db) =>
        new(db, new LegacyReusableComponentPayloadMigrator());

    private static LegacyComponentMigrationExpectedCounts ExpectedCounts(
        int sourceRevisionCount = 2) =>
        new(sourceRevisionCount, 0, 0, 0, 0);

    private static DbContextOptions<DiscoverJesusDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<DiscoverJesusDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings =>
                warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static async Task SeedAsync(DbContextOptions<DiscoverJesusDbContext> options)
    {
        await using var db = new DiscoverJesusDbContext(options);
        var page = new Page
        {
            Slug = "home",
            Title = "Home",
            PublicId = PagePublicId
        };
        var definition = new ReusableComponentDefinition
        {
            Name = "Legacy heading",
            NormalizedName = "legacy heading",
            CreatedAt = DateTimeOffset.UtcNow,
            PublicId = ComponentPublicId
        };
        db.Pages.Add(page);
        db.ReusableComponents.Add(definition);
        await db.SaveChangesAsync();

        db.ReusableComponentRevisions.AddRange(
            new ReusableComponentRevision
            {
                DefinitionId = definition.Id,
                Locale = "en",
                RevisionNumber = 1,
                ContentJson = Payload("First"),
                PublishedAt = DateTimeOffset.UtcNow.AddDays(-2)
            },
            new ReusableComponentRevision
            {
                DefinitionId = definition.Id,
                Locale = "en",
                RevisionNumber = 2,
                ContentJson = Payload("Second"),
                PublishedAt = DateTimeOffset.UtcNow.AddDays(-1)
            });
        await db.SaveChangesAsync();
    }

    private static string Payload(string text) =>
        JsonSerializer.Serialize(new
        {
            type = "text",
            source = new
            {
                legacyType = "Text",
                page = "home",
                key = "headline",
                componentId = 5,
                legacyProps = new { text, attribute = "medium" }
            }
        });
}
