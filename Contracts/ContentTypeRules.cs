namespace DiscoverJesus.Api.Contracts;

internal static class ContentTypeRules
{
    private static readonly string[] NodeTypeValues =
    [
        "text", "image", "button", "stack", "row", "column", "conditional", "loop",
        "spacer", "divider", "modal"
    ];

    private static readonly string[] LegacyManagementTypeValues =
    [
        "Button", "NBButton", "Text", "Header", "BibleVerse", "title", "List", "NavBar", "Media"
    ];

    private static readonly HashSet<string> NodeTypes = new(NodeTypeValues, StringComparer.Ordinal);
    private static readonly HashSet<string> LegacyManagementTypes =
        new(LegacyManagementTypeValues, StringComparer.Ordinal);

    internal static IEnumerable<string> ManagementComponentTypes =>
        NodeTypeValues.Concat(LegacyManagementTypeValues);

    internal static bool IsSupportedNodeType(string? type) =>
        type is not null && NodeTypes.Contains(type);

    internal static bool IsSupportedManagementType(string? type) =>
        type is not null && (NodeTypes.Contains(type) || LegacyManagementTypes.Contains(type));
}
