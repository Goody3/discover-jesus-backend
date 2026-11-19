using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiscoverJesus.Api.Services;

public sealed record LegacyComponentRevisionInput(
    int RevisionId,
    int DefinitionId,
    string Locale,
    int RevisionNumber,
    string ContentJson);

public sealed record LegacyComponentMigrationPage(int Id, Guid PublicId);

public sealed record LegacySourceComponent(int PageId, string Type, int SortOrder);

public sealed record LegacyComponentMigrationEntry(
    int SourceRevisionId,
    int DefinitionId,
    string Locale,
    int SourceRevisionNumber,
    string? TransformedContent,
    IReadOnlyList<string> Errors)
{
    public bool IsReady => TransformedContent is not null && Errors.Count == 0;
}

public sealed record LegacyComponentMigrationReport(
    IReadOnlyList<LegacyComponentMigrationEntry> Entries,
    int MappedSourcePageCount,
    int NavigationTargetCount,
    int ListItemCount,
    int NavigationBarCount,
    int NavigationBarChildCount)
{
    public int ReadyCount => Entries.Count(entry => entry.IsReady);

    public int BlockedCount => Entries.Count - ReadyCount;
}

public sealed class LegacyReusableComponentPayloadMigrator
{
    public LegacyComponentMigrationReport Migrate(
        IReadOnlyList<LegacyComponentRevisionInput> revisions,
        IReadOnlyDictionary<string, LegacyComponentMigrationPage> pagesBySlug,
        IReadOnlyDictionary<int, LegacySourceComponent> sourceComponentsById)
    {
        var parsed = revisions.Select(Parse).ToArray();
        var currentButtons = parsed
            .GroupBy(item => new { item.Input.DefinitionId, item.Input.Locale })
            .Select(group => group.MaxBy(item => item.Input.RevisionNumber)!)
            .Where(item => item.Payload?.LegacyType == "NBButton")
            .GroupBy(item => new
            {
                item.Payload!.SourcePageSlug,
                item.Input.Locale
            })
            .ToDictionary(
                group => (group.Key.SourcePageSlug, group.Key.Locale),
                group => group.OrderBy(item => item.Input.DefinitionId).ToArray());
        var navigationBarDefinitionsByGroup = parsed
            .Where(item => item.Payload?.LegacyType == "NavBar")
            .GroupBy(item => new { item.Payload!.SourcePageSlug, item.Input.Locale })
            .ToDictionary(
                group => (group.Key.SourcePageSlug, group.Key.Locale),
                group => group.Select(item => item.Input.DefinitionId).Distinct().ToArray());

        var entries = new List<LegacyComponentMigrationEntry>(parsed.Length);
        var mappedSourcePageCount = 0;
        var navigationTargetCount = 0;
        var listItemCount = 0;
        var navigationBarCount = 0;
        var navigationBarChildCount = 0;

        foreach (var item in parsed)
        {
            var errors = new List<string>(item.Errors);
            JsonObject? transformed = null;
            if (item.Payload is not null)
            {
                if (pagesBySlug.TryGetValue(item.Payload.SourcePageSlug, out var page))
                {
                    mappedSourcePageCount++;
                    transformed = Transform(
                        item,
                        page,
                        pagesBySlug,
                        sourceComponentsById,
                        currentButtons,
                        navigationBarDefinitionsByGroup,
                        errors,
                        out var targetWasMapped,
                        out var listItems,
                        out var children);
                    if (targetWasMapped)
                    {
                        navigationTargetCount++;
                    }

                    listItemCount += listItems;
                    if (item.Payload.LegacyType == "NavBar" && errors.Count == 0)
                    {
                        navigationBarCount++;
                        navigationBarChildCount += children;
                    }
                }
                else
                {
                    errors.Add($"source.page '{item.Payload.SourcePageSlug}' does not resolve to a page.");
                }
            }

            entries.Add(new LegacyComponentMigrationEntry(
                item.Input.RevisionId,
                item.Input.DefinitionId,
                item.Input.Locale,
                item.Input.RevisionNumber,
                errors.Count == 0 ? transformed?.ToJsonString() : null,
                errors));
        }

        return new LegacyComponentMigrationReport(
            entries,
            mappedSourcePageCount,
            navigationTargetCount,
            listItemCount,
            navigationBarCount,
            navigationBarChildCount);
    }

    private static JsonObject? Transform(
        ParsedRevision item,
        LegacyComponentMigrationPage page,
        IReadOnlyDictionary<string, LegacyComponentMigrationPage> pagesBySlug,
        IReadOnlyDictionary<int, LegacySourceComponent> sourceComponentsById,
        IReadOnlyDictionary<(string SourcePageSlug, string Locale), ParsedRevision[]> currentButtons,
        IReadOnlyDictionary<(string SourcePageSlug, string Locale), int[]> navigationBarDefinitionsByGroup,
        List<string> errors,
        out bool targetWasMapped,
        out int listItems,
        out int children)
    {
        targetWasMapped = false;
        listItems = 0;
        children = 0;
        var payload = item.Payload!;
        var expectedType = GetExpectedType(payload.LegacyType);
        if (expectedType is null)
        {
            errors.Add($"source.legacyType '{payload.LegacyType}' is not an approved mapping.");
        }
        else if (payload.Type != expectedType)
        {
            errors.Add($"type '{payload.Type}' does not match the approved type '{expectedType}' for '{payload.LegacyType}'.");
        }

        JsonArray? navigationChildren = null;
        if (payload.LegacyType == "NavBar")
        {
            var group = (payload.SourcePageSlug, item.Input.Locale);
            if (!navigationBarDefinitionsByGroup.TryGetValue(group, out var definitions)
                || definitions.Length != 1)
            {
                errors.Add("the source page and locale must identify exactly one NavBar definition.");
            }

            if (!currentButtons.TryGetValue(group, out var buttons) || buttons.Length == 0)
            {
                errors.Add("no current NBButton definitions share this source page and locale.");
            }
            else
            {
                var orderedButtons = new List<(int SortOrder, ParsedRevision Item)>(buttons.Length);
                foreach (var button in buttons)
                {
                    var source = button.Payload!;
                    if (!sourceComponentsById.TryGetValue(source.SourceComponentId, out var sourceComponent))
                    {
                        errors.Add($"NBButton source component {source.SourceComponentId} has no original component row for ordering.");
                        continue;
                    }

                    if (sourceComponent.PageId != page.Id || sourceComponent.Type != "NBButton")
                    {
                        errors.Add($"NBButton source component {source.SourceComponentId} does not match its source page and type.");
                        continue;
                    }

                    orderedButtons.Add((sourceComponent.SortOrder, button));
                }

                if (orderedButtons.Select(item => item.SortOrder).Distinct().Count() != orderedButtons.Count)
                {
                    errors.Add("NBButton source components have duplicate SortOrder values; their original order is ambiguous.");
                }
                else if (orderedButtons.Count == buttons.Length)
                {
                    navigationChildren = [];
                    foreach (var (_, button) in orderedButtons.OrderBy(item => item.SortOrder))
                    {
                        var buttonErrors = new List<string>();
                        var buttonPage = pagesBySlug[button.Payload!.SourcePageSlug];
                        var child = Transform(
                            button,
                            buttonPage,
                            pagesBySlug,
                            sourceComponentsById,
                            currentButtons,
                            navigationBarDefinitionsByGroup,
                            buttonErrors,
                            out var childTargetWasMapped,
                            out _,
                            out _);
                        if (buttonErrors.Count > 0 || child is null || !childTargetWasMapped)
                        {
                            errors.AddRange(buttonErrors.Select(error =>
                                $"NBButton revision {button.Input.RevisionId}: {error}"));
                            continue;
                        }

                        navigationChildren.Add(child);
                    }

                    children = navigationChildren.Count;
                }
            }
        }

        var node = new JsonObject
        {
            ["type"] = expectedType,
            ["sourcePageId"] = page.PublicId.ToString("D"),
            ["legacySource"] = new JsonObject
            {
                ["key"] = payload.SourceKey,
                ["componentId"] = JsonNode.Parse(payload.SourceComponentIdJson)
            }
        };
        if (payload.Root["exampleName"] is { } exampleName)
        {
            node["exampleName"] = exampleName.DeepClone();
        }

        switch (payload.LegacyType)
        {
            case "Button":
            case "NBButton":
            {
                var text = RequiredString(payload.LegacyProps, "text", "source.legacyProps.text", errors);
                var target = RequiredString(payload.LegacyProps, "target", "source.legacyProps.target", errors);
                if (target is not null)
                {
                    if (pagesBySlug.TryGetValue(target, out var targetPage))
                    {
                        targetWasMapped = true;
                        node["action"] = new JsonObject
                        {
                            ["type"] = "navigate",
                            ["pageId"] = targetPage.PublicId.ToString("D")
                        };
                    }
                    else
                    {
                        errors.Add($"navigation target '{target}' does not resolve to a page.");
                    }
                }

                if (text is not null)
                {
                    node["value"] = text;
                }

                if (payload.LegacyType == "NBButton")
                {
                    node["role"] = "navigation-item";
                    CopyLegacyAttribute(payload.LegacyProps, "text", node, errors);
                }

                break;
            }
            case "Text":
            {
                var text = RequiredString(payload.LegacyProps, "text", "source.legacyProps.text", errors);
                var size = RequiredString(payload.LegacyProps, "attribute", "source.legacyProps.attribute", errors);
                if (text is not null)
                {
                    node["value"] = text;
                }

                var normalizedSize = size switch
                {
                    "Small" or "small" => "small",
                    "Medium" or "medium" => "medium",
                    "Large" or "large" => "large",
                    _ => null
                };
                if (normalizedSize is null)
                {
                    errors.Add($"Text attribute '{size}' is not an approved text size.");
                }
                else
                {
                    node["style"] = new JsonObject { ["textSize"] = normalizedSize };
                }

                break;
            }
            case "Header":
            {
                var text = RequiredString(payload.LegacyProps, "text", "source.legacyProps.text", errors);
                var level = RequiredString(payload.LegacyProps, "attribute", "source.legacyProps.attribute", errors);
                if (text is not null)
                {
                    node["value"] = text;
                }

                var headingLevel = level switch
                {
                    "H1" => 1,
                    "H2" => 2,
                    "H3" => 3,
                    _ => 0
                };
                if (headingLevel == 0)
                {
                    errors.Add($"Header attribute '{level}' is not an approved heading level.");
                }
                else
                {
                    node["role"] = "heading";
                    node["headingLevel"] = headingLevel;
                }

                break;
            }
            case "BibleVerse":
            {
                var text = RequiredString(payload.LegacyProps, "text", "source.legacyProps.text", errors);
                var reference = RequiredString(payload.LegacyProps, "reference", "source.legacyProps.reference", errors);
                var version = RequiredString(payload.LegacyProps, "version", "source.legacyProps.version", errors);
                if (text is not null)
                {
                    node["value"] = text;
                }

                if (reference is not null)
                {
                    node["reference"] = reference;
                }

                if (version is not null)
                {
                    node["version"] = version;
                }

                node["role"] = "scripture-reference";
                break;
            }
            case "title":
            {
                var text = RequiredString(payload.LegacyProps, "text", "source.legacyProps.text", errors);
                var alignment = RequiredString(payload.LegacyProps, "alignment", "source.legacyProps.alignment", errors);
                var fontSize = RequiredString(payload.LegacyProps, "fontSize", "source.legacyProps.fontSize", errors);
                if (text is not null)
                {
                    node["value"] = text;
                }

                if (alignment is not null && fontSize is not null)
                {
                    node["style"] = new JsonObject
                    {
                        ["alignment"] = alignment,
                        ["fontSize"] = fontSize
                    };
                }

                node["role"] = "title";
                break;
            }
            case "List":
            {
                var label = RequiredString(payload.LegacyProps, "text", "source.legacyProps.text", errors);
                var attribute = RequiredString(payload.LegacyProps, "attribute", "source.legacyProps.attribute", errors);
                if (label is not null)
                {
                    node["label"] = label;
                }

                var listStyle = attribute switch
                {
                    "ol" => "ordered",
                    "ul" => "unordered",
                    _ => null
                };
                if (listStyle is null)
                {
                    errors.Add($"List attribute '{attribute}' is not an approved list style.");
                }

                if (payload.LegacyProps["list"] is not JsonArray list)
                {
                    errors.Add("source.legacyProps.list must be an array.");
                }
                else
                {
                    var migratedChildren = new JsonArray();
                    foreach (var listItem in list)
                    {
                        if (listItem is not JsonValue value || !value.TryGetValue<string>(out var itemText))
                        {
                            errors.Add("every source.legacyProps.list item must be a string.");
                            continue;
                        }

                        migratedChildren.Add(new JsonObject
                        {
                            ["type"] = "text",
                            ["value"] = itemText
                        });
                        listItems++;
                    }

                    node["children"] = migratedChildren;
                    ValidateListParity(payload.Root, list, errors);
                }

                if (listStyle is not null)
                {
                    node["style"] = new JsonObject { ["listStyle"] = listStyle };
                }

                node["role"] = "list";
                break;
            }
            case "NavBar":
                node["role"] = "navigation";
                if (navigationChildren is not null)
                {
                    node["children"] = navigationChildren;
                }

                break;
            case "Media":
            {
                var text = RequiredString(payload.LegacyProps, "text", "source.legacyProps.text", errors);
                var url = RequiredString(payload.LegacyProps, "url", "source.legacyProps.url", errors);
                if (text is not null)
                {
                    node["altText"] = text;
                }

                if (url is not null)
                {
                    node["src"] = url;
                }

                CopyLegacyAttribute(payload.LegacyProps, "image", node, errors);
                break;
            }
        }

        return errors.Count == 0 ? node : null;
    }

    private static ParsedRevision Parse(LegacyComponentRevisionInput input)
    {
        var errors = new List<string>();
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(input.ContentJson) as JsonObject;
        }
        catch (JsonException)
        {
            errors.Add("content JSON is invalid.");
            return new ParsedRevision(input, null, errors);
        }

        if (root is null)
        {
            errors.Add("content JSON must be an object.");
            return new ParsedRevision(input, null, errors);
        }

        if (root["source"] is not JsonObject source)
        {
            errors.Add("source must be an object.");
            return new ParsedRevision(input, null, errors);
        }

        if (source["legacyProps"] is not JsonObject legacyProps)
        {
            errors.Add("source.legacyProps must be an object.");
            return new ParsedRevision(input, null, errors);
        }

        var type = RequiredString(root, "type", "type", errors);
        var legacyType = RequiredString(source, "legacyType", "source.legacyType", errors);
        var sourcePageSlug = RequiredString(source, "page", "source.page", errors);
        var sourceKey = RequiredString(source, "key", "source.key", errors);
        var componentId = source["componentId"];
        var componentIdJson = componentId?.ToJsonString();
        if (componentId is null || componentId.GetValueKind() != JsonValueKind.Number
            || !int.TryParse(
                componentIdJson,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var sourceComponentId))
        {
            errors.Add("source.componentId must be a numeric identifier.");
            sourceComponentId = 0;
        }

        if (string.IsNullOrWhiteSpace(input.Locale))
        {
            errors.Add("revision locale is required.");
        }

        if (errors.Count > 0 || type is null || legacyType is null || sourcePageSlug is null
            || sourceKey is null || componentIdJson is null)
        {
            return new ParsedRevision(input, null, errors);
        }

        return new ParsedRevision(
            input,
            new LegacyPayload(
                root,
                legacyProps,
                type,
                legacyType,
                sourcePageSlug,
                sourceKey,
                sourceComponentId,
                componentIdJson),
            errors);
    }

    private static string? GetExpectedType(string legacyType) =>
        legacyType switch
        {
            "Button" or "NBButton" => "button",
            "Text" or "Header" or "BibleVerse" or "title" => "text",
            "List" or "NavBar" => "stack",
            "Media" => "image",
            _ => null
        };

    private static string? RequiredString(
        JsonObject source,
        string property,
        string path,
        List<string> errors)
    {
        if (source.TryGetPropertyValue(property, out var value)
            && value is JsonValue jsonValue
            && jsonValue.TryGetValue<string>(out var result)
            && result is not null)
        {
            return result;
        }

        errors.Add($"{path} must be a string.");
        return null;
    }

    private static void CopyLegacyAttribute(
        JsonObject legacyProps,
        string expectedValue,
        JsonObject node,
        List<string> errors)
    {
        var attribute = RequiredString(
            legacyProps,
            "attribute",
            "source.legacyProps.attribute",
            errors);
        if (attribute != expectedValue)
        {
            errors.Add($"legacy attribute '{attribute}' does not match the approved value '{expectedValue}'.");
            return;
        }

        node["legacyAttribute"] = attribute;
    }

    private static void ValidateListParity(
        JsonObject root,
        JsonArray legacyItems,
        List<string> errors)
    {
        if (root["children"] is not JsonArray existingChildren)
        {
            errors.Add("existing children must be an array to verify List order and value parity.");
            return;
        }

        if (existingChildren.Count != legacyItems.Count)
        {
            errors.Add("existing List children do not match the legacy list item count.");
            return;
        }

        for (var index = 0; index < legacyItems.Count; index++)
        {
            if (legacyItems[index] is not JsonValue legacyValue
                || !legacyValue.TryGetValue<string>(out var expectedText)
                || existingChildren[index] is not JsonObject child
                || child["type"] is not JsonValue childType
                || !childType.TryGetValue<string>(out var childTypeText)
                || childTypeText != "text"
                || child["value"] is not JsonValue childValue
                || !childValue.TryGetValue<string>(out var actualText)
                || actualText != expectedText)
            {
                errors.Add($"existing List child at index {index} does not match the legacy list item.");
            }
        }
    }

    private sealed record ParsedRevision(
        LegacyComponentRevisionInput Input,
        LegacyPayload? Payload,
        IReadOnlyList<string> Errors);

    private sealed record LegacyPayload(
        JsonObject Root,
        JsonObject LegacyProps,
        string Type,
        string LegacyType,
        string SourcePageSlug,
        string SourceKey,
        int SourceComponentId,
        string SourceComponentIdJson);
}
