using System.Text.Json;
using DiscoverJesus.Api.Data;
using DiscoverJesus.Api.Services;
using Microsoft.EntityFrameworkCore;

var usesSnapshotFile = args.Length == 2 && args[0] == "--dry-run-json";
var isPreproductionApply = args.Length == 3
    && args[0] == "--apply-preproduction"
    && args[1] == "--batch-id"
    && Guid.TryParse(args[2], out _);
if (!usesSnapshotFile && !isPreproductionApply && (args.Length != 1 || args[0] != "--dry-run"))
{
    Console.Error.WriteLine(
        "Use --dry-run, --dry-run-json <snapshot-file>, or --apply-preproduction --batch-id <guid>.");
    return 2;
}

const int expectedRevisionCount = 55;
const int expectedNavigationTargetCount = 24;
const int expectedListItemCount = 12;
const int expectedNavigationBarCount = 2;
const int expectedNavigationBarChildCount = 4;
const string migrationVersion = "epic003-v1";

if (isPreproductionApply)
{
    var migrationBatchId = Guid.Parse(args[2]);
    var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.Error.WriteLine("Set ConnectionStrings__DefaultConnection to the hosted development database.");
        return 2;
    }

    var connection = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
    if (!string.Equals(connection.DataSource, "sql8010.site4now.net", StringComparison.OrdinalIgnoreCase)
        || !string.Equals(connection.InitialCatalog, "db_9b2441_discoverjesus", StringComparison.Ordinal))
    {
        Console.Error.WriteLine(
            "Preproduction apply is restricted to sql8010.site4now.net/db_9b2441_discoverjesus.");
        return 2;
    }

    var options = new DbContextOptionsBuilder<DiscoverJesusDbContext>()
        .UseSqlServer(connectionString)
        .Options;
    await using var db = new DiscoverJesusDbContext(options);
    const string requiredLineageMigration = "20261115013518_AddReusableComponentRevisionLineage";
    var appliedMigrations = await db.Database.GetAppliedMigrationsAsync();
    if (!appliedMigrations.Contains(requiredLineageMigration, StringComparer.Ordinal))
    {
        Console.Error.WriteLine(
            $"Apply EF migration '{requiredLineageMigration}' before running the content migration. No content was written.");
        return 2;
    }

    var result = await new LegacyReusableComponentRevisionMigrationService(
        db,
        new LegacyReusableComponentPayloadMigrator())
        .ApplyAsync(
            migrationVersion,
            migrationBatchId,
            new LegacyComponentMigrationExpectedCounts(
                expectedRevisionCount,
                expectedNavigationTargetCount,
                expectedListItemCount,
                expectedNavigationBarCount,
                expectedNavigationBarChildCount),
            CancellationToken.None);
    var applyOutput = new
    {
        mode = "preproduction-apply",
        writesPerformed = result.CreatedRevisionCount > 0,
        result.MigrationVersion,
        result.MigrationBatchId,
        result.MigratedAt,
        result.SourceRevisionCount,
        result.CreatedRevisionCount,
        result.ExistingRevisionCount,
        result.MappingReport.MappedSourcePageCount,
        result.MappingReport.NavigationTargetCount,
        result.MappingReport.ListItemCount,
        result.MappingReport.NavigationBarCount,
        result.MappingReport.NavigationBarChildCount,
        result.MappingReport.ReadyCount,
        result.MappingReport.BlockedCount
    };
    Console.WriteLine(JsonSerializer.Serialize(applyOutput, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    }));
    return 0;
}

IReadOnlyList<PageSnapshot> pageRows;
IReadOnlyList<SourceComponentSnapshot> sourceComponents;
IReadOnlyList<LegacyComponentRevisionInput> revisions;

if (usesSnapshotFile)
{
    await using var snapshotStream = File.OpenRead(args[1]);
    var snapshot = await JsonSerializer.DeserializeAsync<DatabaseSnapshot>(
        snapshotStream,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    if (snapshot is null)
    {
        Console.Error.WriteLine("The snapshot file does not contain a valid database snapshot.");
        return 2;
    }

    pageRows = snapshot.Pages;
    sourceComponents = snapshot.Components;
    revisions = snapshot.Revisions;
}
else
{
    var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.Error.WriteLine("Set ConnectionStrings__DefaultConnection to the hosted development database.");
        return 2;
    }

    var options = new DbContextOptionsBuilder<DiscoverJesusDbContext>()
        .UseSqlServer(connectionString)
        .Options;
    await using var db = new DiscoverJesusDbContext(options);
    pageRows = await db.Pages.AsNoTracking()
        .Select(page => new PageSnapshot(page.Id, page.Slug, page.PublicId))
        .ToListAsync();
    sourceComponents = await db.Components.AsNoTracking()
        .Select(component => new SourceComponentSnapshot(
            component.Id,
            component.PageId,
            component.Type,
            component.SortOrder))
        .ToListAsync();
    revisions = await db.ReusableComponentRevisions.AsNoTracking()
        .Select(revision => new LegacyComponentRevisionInput(
            revision.Id,
            revision.DefinitionId,
            revision.Locale,
            revision.RevisionNumber,
            revision.ContentJson))
        .ToListAsync();
}

var pagesBySlug = pageRows.ToDictionary(
    page => page.Slug,
    page => new LegacyComponentMigrationPage(page.Id, page.PublicId),
    StringComparer.Ordinal);
var sourceComponentsById = sourceComponents.ToDictionary(
        component => component.Id,
        component => new LegacySourceComponent(
            component.PageId,
            component.Type,
            component.SortOrder));

var report = new LegacyReusableComponentPayloadMigrator().Migrate(
    revisions,
    pagesBySlug,
    sourceComponentsById);

var matchesInventory =
    revisions.Count == expectedRevisionCount
    && report.MappedSourcePageCount == expectedRevisionCount
    && report.NavigationTargetCount == expectedNavigationTargetCount
    && report.ListItemCount == expectedListItemCount
    && report.NavigationBarCount == expectedNavigationBarCount
    && report.NavigationBarChildCount == expectedNavigationBarChildCount
    && report.BlockedCount == 0;
var output = new
{
    mode = "dry-run",
    writesPerformed = false,
    matchesInventory,
    expectedRevisionCount,
    revisionCount = revisions.Count,
    report.MappedSourcePageCount,
    report.NavigationTargetCount,
    report.ListItemCount,
    report.NavigationBarCount,
    report.NavigationBarChildCount,
    report.ReadyCount,
    report.BlockedCount,
    blockedRevisions = report.Entries
        .Where(entry => !entry.IsReady)
        .Select(entry => new
        {
            entry.SourceRevisionId,
            entry.DefinitionId,
            entry.Locale,
            entry.SourceRevisionNumber,
            entry.Errors
        })
};

Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true
}));
return matchesInventory ? 0 : 1;

internal sealed record DatabaseSnapshot(
    IReadOnlyList<PageSnapshot> Pages,
    IReadOnlyList<SourceComponentSnapshot> Components,
    IReadOnlyList<LegacyComponentRevisionInput> Revisions);

internal sealed record PageSnapshot(int Id, string Slug, Guid PublicId);

internal sealed record SourceComponentSnapshot(int Id, int PageId, string Type, int SortOrder);
