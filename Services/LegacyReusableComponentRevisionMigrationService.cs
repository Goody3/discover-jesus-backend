using System.Data;
using DiscoverJesus.Api.Data;
using DiscoverJesus.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DiscoverJesus.Api.Services;

public sealed record LegacyComponentMigrationApplyResult(
    Guid MigrationBatchId,
    string MigrationVersion,
    DateTimeOffset MigratedAt,
    int SourceRevisionCount,
    int CreatedRevisionCount,
    int ExistingRevisionCount,
    LegacyComponentMigrationReport MappingReport);

public sealed record LegacyComponentMigrationExpectedCounts(
    int SourceRevisionCount,
    int NavigationTargetCount,
    int ListItemCount,
    int NavigationBarCount,
    int NavigationBarChildCount);

public sealed class LegacyReusableComponentRevisionMigrationService(
    DiscoverJesusDbContext db,
    LegacyReusableComponentPayloadMigrator payloadMigrator)
{
    public async Task<LegacyComponentMigrationApplyResult> ApplyAsync(
        string migrationVersion,
        Guid migrationBatchId,
        LegacyComponentMigrationExpectedCounts expectedCounts,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(migrationVersion) || migrationVersion.Length > 100)
        {
            throw new ArgumentException(
                "Migration version must contain between 1 and 100 characters.",
                nameof(migrationVersion));
        }

        if (migrationBatchId == Guid.Empty)
        {
            throw new ArgumentException("Migration batch ID must not be empty.", nameof(migrationBatchId));
        }

        if (expectedCounts.SourceRevisionCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedCounts),
                "Expected source revision count must be positive.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var allRevisions = await db.ReusableComponentRevisions
            .OrderBy(revision => revision.DefinitionId)
            .ThenBy(revision => revision.Locale)
            .ThenBy(revision => revision.RevisionNumber)
            .ToListAsync(cancellationToken);
        var migratedRevisionIds = await db.ReusableComponentRevisionLineages
            .Select(lineage => lineage.MigratedRevisionId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var migratedRevisionIdSet = migratedRevisionIds.ToHashSet();
        var sourceRevisions = allRevisions
            .Where(revision => !migratedRevisionIdSet.Contains(revision.Id))
            .ToList();
        if (sourceRevisions.Count != expectedCounts.SourceRevisionCount)
        {
            throw new InvalidOperationException(
                $"Expected {expectedCounts.SourceRevisionCount} source revisions but found {sourceRevisions.Count}; no migration was applied.");
        }

        var pages = await db.Pages.AsNoTracking()
            .Select(page => new
            {
                page.Slug,
                Page = new LegacyComponentMigrationPage(page.Id, page.PublicId)
            })
            .ToListAsync(cancellationToken);
        var pagesBySlug = pages.ToDictionary(item => item.Slug, item => item.Page, StringComparer.Ordinal);
        var sourceComponents = await db.Components.AsNoTracking()
            .ToDictionaryAsync(
                component => component.Id,
                component => new LegacySourceComponent(
                    component.PageId,
                    component.Type,
                    component.SortOrder),
                cancellationToken);
        var inputs = sourceRevisions
            .Select(revision => new LegacyComponentRevisionInput(
                revision.Id,
                revision.DefinitionId,
                revision.Locale,
                revision.RevisionNumber,
                revision.ContentJson))
            .ToArray();
        var mappingReport = payloadMigrator.Migrate(inputs, pagesBySlug, sourceComponents);
        if (mappingReport.BlockedCount != 0
            || mappingReport.ReadyCount != expectedCounts.SourceRevisionCount
            || mappingReport.MappedSourcePageCount != expectedCounts.SourceRevisionCount
            || mappingReport.NavigationTargetCount != expectedCounts.NavigationTargetCount
            || mappingReport.ListItemCount != expectedCounts.ListItemCount
            || mappingReport.NavigationBarCount != expectedCounts.NavigationBarCount
            || mappingReport.NavigationBarChildCount != expectedCounts.NavigationBarChildCount)
        {
            var blockers = string.Join(
                Environment.NewLine,
                mappingReport.Entries
                    .Where(entry => !entry.IsReady)
                    .Select(entry =>
                        $"Revision {entry.SourceRevisionId}: {string.Join(" ", entry.Errors)}"));
            throw new InvalidOperationException(
                $"The mapping preflight did not match the approved inventory; no migration was applied. "
                + $"Revisions {mappingReport.ReadyCount}/{expectedCounts.SourceRevisionCount}, "
                + $"source pages {mappingReport.MappedSourcePageCount}/{expectedCounts.SourceRevisionCount}, "
                + $"navigation targets {mappingReport.NavigationTargetCount}/{expectedCounts.NavigationTargetCount}, "
                + $"List items {mappingReport.ListItemCount}/{expectedCounts.ListItemCount}, "
                + $"NavBars {mappingReport.NavigationBarCount}/{expectedCounts.NavigationBarCount}, "
                + $"NavBar children {mappingReport.NavigationBarChildCount}/{expectedCounts.NavigationBarChildCount}, "
                + $"blocked {mappingReport.BlockedCount}.{Environment.NewLine}{blockers}");
        }

        var sourceRevisionIds = sourceRevisions.Select(revision => revision.Id).ToArray();
        var existingLineages = await db.ReusableComponentRevisionLineages
            .Where(lineage => lineage.MigrationVersion == migrationVersion
                && sourceRevisionIds.Contains(lineage.SourceRevisionId))
            .Include(lineage => lineage.MigratedRevision)
            .ToListAsync(cancellationToken);
        var existingBySourceId = existingLineages.ToDictionary(lineage => lineage.SourceRevisionId);
        if (existingLineages.Count != 0 && existingLineages.Count != sourceRevisions.Count)
        {
            throw new InvalidOperationException(
                $"Found {existingLineages.Count} of {sourceRevisions.Count} lineage records for migration version '{migrationVersion}'; partial migrations are not safe to repair automatically.");
        }

        if (existingLineages.Select(lineage => lineage.MigrationBatchId).Distinct().Skip(1).Any()
            || existingLineages.Select(lineage => lineage.MigratedAt).Distinct().Skip(1).Any())
        {
            throw new InvalidOperationException(
                $"Existing lineage records for migration version '{migrationVersion}' do not share one batch and migration time.");
        }

        var entriesBySourceId = mappingReport.Entries.ToDictionary(entry => entry.SourceRevisionId);
        foreach (var lineage in existingLineages)
        {
            var source = sourceRevisions.Single(revision => revision.Id == lineage.SourceRevisionId);
            var entry = entriesBySourceId[source.Id];
            var migrated = lineage.MigratedRevision;
            if (!entry.IsReady
                || migrated.DefinitionId != source.DefinitionId
                || !string.Equals(migrated.Locale, source.Locale, StringComparison.Ordinal)
                || migrated.RevisionNumber <= source.RevisionNumber
                || !string.Equals(migrated.ContentJson, entry.TransformedContent, StringComparison.Ordinal)
                || migrated.PublishedAt != lineage.MigratedAt)
            {
                throw new InvalidOperationException(
                    $"Existing lineage for source revision {source.Id} does not match the approved transformation; no migration was applied.");
            }
        }

        var effectiveBatchId = existingLineages.Count > 0
            ? existingLineages[0].MigrationBatchId
            : migrationBatchId;
        var migratedAt = existingLineages.Count > 0
            ? existingLineages[0].MigratedAt
            : DateTimeOffset.UtcNow;
        var revisionNumbers = allRevisions
            .GroupBy(revision => (revision.DefinitionId, revision.Locale))
            .ToDictionary(
                group => group.Key,
                group => group.Max(revision => revision.RevisionNumber));
        var pendingLineages = new List<(ReusableComponentRevision Source, ReusableComponentRevision Migrated)>();
        foreach (var source in sourceRevisions)
        {
            if (existingBySourceId.ContainsKey(source.Id))
            {
                continue;
            }

            var entry = entriesBySourceId[source.Id];
            var key = (source.DefinitionId, source.Locale);
            var revision = new ReusableComponentRevision
            {
                DefinitionId = source.DefinitionId,
                Locale = source.Locale,
                RevisionNumber = checked(++revisionNumbers[key]),
                ContentJson = entry.TransformedContent
                    ?? throw new InvalidOperationException(
                        $"Source revision {source.Id} has no transformed content after a successful preflight."),
                PublishedAt = migratedAt
            };
            db.ReusableComponentRevisions.Add(revision);
            pendingLineages.Add((source, revision));
        }

        if (pendingLineages.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            db.ReusableComponentRevisionLineages.AddRange(
                pendingLineages.Select(pair => new ReusableComponentRevisionLineage
                {
                    SourceRevisionId = pair.Source.Id,
                    MigratedRevisionId = pair.Migrated.Id,
                    MigrationVersion = migrationVersion,
                    MigrationBatchId = migrationBatchId,
                    MigratedAt = migratedAt
                }));
            await db.SaveChangesAsync(cancellationToken);
        }

        var persistedLineages = await db.ReusableComponentRevisionLineages
            .Where(lineage => lineage.MigrationVersion == migrationVersion
                && sourceRevisionIds.Contains(lineage.SourceRevisionId))
            .ToListAsync(cancellationToken);
        if (persistedLineages.Count != sourceRevisions.Count
            || persistedLineages.Select(lineage => lineage.SourceRevisionId).Distinct().Count()
                != sourceRevisions.Count)
        {
            throw new InvalidOperationException(
                $"Expected one persisted lineage for each of {sourceRevisions.Count} source revisions but found {persistedLineages.Count}; the transaction was rolled back.");
        }

        await transaction.CommitAsync(cancellationToken);
        return new LegacyComponentMigrationApplyResult(
            effectiveBatchId,
            migrationVersion,
            migratedAt,
            sourceRevisions.Count,
            pendingLineages.Count,
            existingLineages.Count,
            mappingReport);
    }
}
