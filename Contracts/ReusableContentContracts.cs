using System.Text.Json;

namespace DiscoverJesus.Api.Contracts;

/// <summary>Creates a named reusable component and its initial locale-specific draft.</summary>
public sealed record CreateReusableComponentRequest(string Name, string Locale, JsonElement Content);

/// <summary>Changes the friendly name of a reusable component.</summary>
public sealed record UpdateReusableComponentNameRequest(string Name);

/// <summary>Represents a reusable component in the component library.</summary>
public sealed record ReusableComponentSummary(
    Guid ComponentId,
    string Name,
    bool IsWithdrawn,
    DateTimeOffset CreatedAt);

/// <summary>Replaces a locale-specific reusable component draft.</summary>
public sealed record UpdateReusableComponentDraftRequest(JsonElement Content);

/// <summary>Represents a reusable component draft.</summary>
public sealed record ReusableComponentDraftResponse(
    Guid ComponentId,
    string Name,
    string Locale,
    JsonElement Content,
    DateTimeOffset UpdatedAt);

/// <summary>Represents an immutable published reusable component revision.</summary>
public sealed record ReusableComponentResponse(
    Guid ComponentId,
    string Name,
    string Locale,
    int Revision,
    JsonElement Content,
    DateTimeOffset PublishedAt);

/// <summary>Replaces a page's locale-specific section draft.</summary>
public sealed record UpdatePageContentDraftRequest(JsonElement Sections);

/// <summary>Represents a page content draft.</summary>
public sealed record PageContentDraftResponse(
    Guid PageId,
    string Locale,
    JsonElement Sections,
    DateTimeOffset UpdatedAt);

/// <summary>Represents a published page content revision with resolved sections.</summary>
public sealed record PageContentResponse(
    Guid Id,
    string Title,
    string Locale,
    int Version,
    JsonElement Sections);

/// <summary>Represents a failed reusable-content operation.</summary>
public sealed record ReusableContentError(
    int StatusCode,
    string Message,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);

/// <summary>Represents the result of a reusable-content operation.</summary>
public sealed record ReusableContentResult<T>(
    T? Value,
    ReusableContentError? Error)
{
    public bool Succeeded => Error is null;

    public static ReusableContentResult<T> Success(T value) => new(value, null);

    public static ReusableContentResult<T> Failure(
        int statusCode,
        string message,
        IReadOnlyDictionary<string, string[]>? validationErrors = null) =>
        new(default, new ReusableContentError(statusCode, message, validationErrors));
}
