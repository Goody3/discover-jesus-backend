using System.Text.Json;
using DiscoverJesus.Api.Services;
using Xunit;

namespace DiscoverJesus.Api.Tests;

public sealed class LegacyReusableComponentPayloadMigratorTests
{
    private static readonly Guid HomePageId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid TargetPageId = Guid.Parse("20000000-0000-0000-0000-000000000002");

    [Fact]
    public void MigratesApprovedLegacyShapesAndRetainsOpaqueMetadata()
    {
        var revisions = new[]
        {
            Revision(1, 1, 1, "Button", 101, new { text = "Open", target = "target" }),
            Revision(2, 2, 1, "NBButton", 102, new { text = "Next", target = "target", attribute = "text" }),
            Revision(3, 3, 1, "Text", 103, new { text = "Body", attribute = "Medium" }, "text"),
            Revision(4, 4, 1, "Header", 104, new { text = "Heading", attribute = "H2" }, "text"),
            Revision(5, 5, 1, "BibleVerse", 105, new { text = "Verse", reference = "John 3:16", version = "NIV" }, "text"),
            Revision(6, 6, 1, "title", 106, new { text = "Title", alignment = "center", fontSize = "large" }, "text"),
            Revision(
                7,
                7,
                1,
                "List",
                107,
                new { text = "Items", list = new[] { "First", "Second" }, attribute = "ol" },
                "stack",
                existingChildren: new[]
                {
                    new { type = "text", value = "First" },
                    new { type = "text", value = "Second" }
                }),
            Revision(9, 9, 1, "Text", 109, new { text = "Canonical medium", attribute = "medium" }, "text"),
            Revision(8, 8, 1, "Media", 108, new { text = "Image description", url = "https://example.org/image.png", attribute = "image" }, "image")
        };

        var report = Migrate(revisions);

        Assert.Equal(9, report.ReadyCount);
        Assert.Equal(0, report.BlockedCount);
        Assert.Equal(2, report.NavigationTargetCount);
        Assert.Equal(2, report.ListItemCount);
        Assert.Equal("navigation-item", Node(report, 2).GetProperty("role").GetString());
        Assert.Equal(
            "text",
            Node(report, 2).GetProperty("legacyAttribute").GetString());
        Assert.Equal("https://example.org/image.png", Node(report, 8).GetProperty("src").GetString());
        Assert.Equal("Image description", Node(report, 8).GetProperty("altText").GetString());
        Assert.Equal("image", Node(report, 8).GetProperty("legacyAttribute").GetString());
        Assert.Equal(
            "10000000-0000-0000-0000-000000000001",
            Node(report, 1).GetProperty("sourcePageId").GetString());
        Assert.Equal("key-101", Node(report, 1).GetProperty("legacySource").GetProperty("key").GetString());
        Assert.Equal(101, Node(report, 1).GetProperty("legacySource").GetProperty("componentId").GetInt32());
        Assert.Equal("Button example", Node(report, 1).GetProperty("exampleName").GetString());

        var children = Node(report, 7).GetProperty("children");
        Assert.Equal("First", children[0].GetProperty("value").GetString());
        Assert.Equal("Second", children[1].GetProperty("value").GetString());
        Assert.Equal("ordered", Node(report, 7).GetProperty("style").GetProperty("listStyle").GetString());
        Assert.Equal("medium", Node(report, 9).GetProperty("style").GetProperty("textSize").GetString());
    }

    [Fact]
    public void NavBarUsesLatestButtonsAndOriginalComponentSortOrder()
    {
        var revisions = new[]
        {
            Revision(1, 10, 1, "NavBar", 200, new { text = "Navigation", attribute = "" }, "stack", key: "nav"),
            Revision(2, 20, 1, "NBButton", 201, new { text = "Old label", target = "target", attribute = "text" }),
            Revision(3, 20, 2, "NBButton", 201, new { text = "Latest label", target = "target", attribute = "text" }),
            Revision(4, 21, 1, "NBButton", 202, new { text = "First in order", target = "target", attribute = "text" })
        };
        var pages = new Dictionary<string, LegacyComponentMigrationPage>(StringComparer.Ordinal)
        {
            ["home"] = new(1, HomePageId),
            ["target"] = new(2, TargetPageId)
        };
        var sourceComponents = new Dictionary<int, LegacySourceComponent>
        {
            [201] = new(1, "NBButton", 5),
            [202] = new(1, "NBButton", 1)
        };

        var report = new LegacyReusableComponentPayloadMigrator().Migrate(
            revisions,
            pages,
            sourceComponents);

        Assert.Equal(4, report.ReadyCount);
        Assert.Equal(1, report.NavigationBarCount);
        Assert.Equal(2, report.NavigationBarChildCount);
        var children = Node(report, 1).GetProperty("children");
        Assert.Equal(2, children.GetArrayLength());
        Assert.Equal("First in order", children[0].GetProperty("value").GetString());
        Assert.Equal("Latest label", children[1].GetProperty("value").GetString());
    }

    [Fact]
    public void BlocksUnmappedNavigationTargets()
    {
        var report = Migrate(
        [
            Revision(1, 1, 1, "Button", 101, new { text = "Open", target = "missing" })
        ]);

        Assert.Equal(0, report.ReadyCount);
        Assert.Equal(1, report.BlockedCount);
        Assert.Contains("does not resolve to a page", string.Join(" ", report.Entries[0].Errors));
    }

    [Fact]
    public void BlocksListWhenExistingChildrenDoNotMatchLegacyOrder()
    {
        var report = Migrate(
        [
            Revision(
                1,
                1,
                1,
                "List",
                101,
                new { text = "Items", list = new[] { "First", "Second" }, attribute = "ol" },
                "stack",
                existingChildren: new[]
                {
                    new { type = "text", value = "Second" },
                    new { type = "text", value = "First" }
                })
        ]);

        Assert.Equal(0, report.ReadyCount);
        Assert.Equal(1, report.BlockedCount);
        Assert.Contains("does not match the legacy list item", string.Join(" ", report.Entries[0].Errors));
    }

    [Fact]
    public void BlocksNavBarWhenOriginalButtonOrderIsAmbiguous()
    {
        var revisions = new[]
        {
            Revision(1, 10, 1, "NavBar", 200, new { text = "Navigation", attribute = "" }, "stack", key: "nav"),
            Revision(2, 20, 1, "NBButton", 201, new { text = "One", target = "target", attribute = "text" }),
            Revision(3, 21, 1, "NBButton", 202, new { text = "Two", target = "target", attribute = "text" })
        };
        var pages = new Dictionary<string, LegacyComponentMigrationPage>(StringComparer.Ordinal)
        {
            ["home"] = new(1, HomePageId),
            ["target"] = new(2, TargetPageId)
        };
        var sourceComponents = new Dictionary<int, LegacySourceComponent>
        {
            [201] = new(1, "NBButton", 1),
            [202] = new(1, "NBButton", 1)
        };

        var report = new LegacyReusableComponentPayloadMigrator().Migrate(
            revisions,
            pages,
            sourceComponents);

        Assert.Equal(2, report.ReadyCount);
        Assert.Equal(1, report.BlockedCount);
        Assert.Contains("duplicate SortOrder", string.Join(" ", report.Entries[0].Errors));
    }

    private static LegacyComponentMigrationReport Migrate(
        IReadOnlyList<LegacyComponentRevisionInput> revisions)
    {
        var pages = new Dictionary<string, LegacyComponentMigrationPage>(StringComparer.Ordinal)
        {
            ["home"] = new(1, HomePageId),
            ["target"] = new(2, TargetPageId)
        };
        return new LegacyReusableComponentPayloadMigrator().Migrate(
            revisions,
            pages,
            new Dictionary<int, LegacySourceComponent>());
    }

    private static JsonElement Node(LegacyComponentMigrationReport report, int revisionId)
    {
        var entry = Assert.Single(report.Entries, entry => entry.SourceRevisionId == revisionId);
        using var document = JsonDocument.Parse(entry.TransformedContent!);
        return document.RootElement.Clone();
    }

    private static LegacyComponentRevisionInput Revision(
        int revisionId,
        int definitionId,
        int revisionNumber,
        string legacyType,
        int sourceComponentId,
        object legacyProps,
        string type = "button",
        string locale = "en",
        string page = "home",
        string? key = null,
        object? existingChildren = null)
    {
        var exampleName = legacyType == "Button" ? "Button example" : null;
        var content = JsonSerializer.Serialize(new
        {
            type,
            exampleName,
            children = existingChildren,
            source = new
            {
                key = key ?? $"key-{sourceComponentId}",
                componentId = sourceComponentId,
                page,
                legacyType,
                legacyProps
            }
        });
        return new LegacyComponentRevisionInput(
            revisionId,
            definitionId,
            locale,
            revisionNumber,
            content);
    }
}
