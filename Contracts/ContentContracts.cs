using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiscoverJesus.Api.Contracts;

/// <summary>Creates or replaces a page in the legacy management API.</summary>
public sealed record PageRequest(
    [property: Required, JsonRequired] string Slug,
    [property: Required, JsonRequired] string Title,
    [property: JsonRequired] bool IsPublished);
/// <summary>Creates or replaces a page-owned inline component.</summary>
public sealed record ComponentRequest(
    [property: Required, JsonRequired] string Key,
    [property: Required, JsonRequired] string Type,
    [property: JsonRequired] JsonElement Props,
    [property: JsonRequired] int SortOrder);
/// <summary>Creates or replaces a component data binding.</summary>
public sealed record BindingRequest(
    [property: Required, JsonRequired] string Name,
    [property: Required, JsonRequired] string Source,
    [property: JsonRequired] JsonElement Config);
/// <summary>Creates or replaces an opaque component action definition.</summary>
public sealed record ActionRequest(
    [property: Required, JsonRequired] string Name,
    [property: Required, JsonRequired] string Type,
    [property: JsonRequired] JsonElement Config);

/// <summary>Identifies a page in the management API and by its stable public identifier.</summary>
public sealed record PageSummary(int Id, Guid PublicId, string Slug, string Title, bool IsPublished);
/// <summary>Represents a page and its legacy inline components.</summary>
public sealed record PageResponse(
    int Id,
    Guid PublicId,
    string Slug,
    string Title,
    bool IsPublished,
    IReadOnlyList<ComponentResponse> Components);
/// <summary>Represents a page-owned inline component.</summary>
public sealed record ComponentResponse(
    int Id,
    string Key,
    string Type,
    JsonElement Props,
    int SortOrder,
    IReadOnlyList<BindingResponse> Bindings,
    IReadOnlyList<ActionResponse> Actions);
/// <summary>Represents a component data binding.</summary>
public sealed record BindingResponse(int Id, string Name, string Source, JsonElement Config);
/// <summary>Represents a stored action definition.</summary>
public sealed record ActionResponse(int Id, string Name, string Type, JsonElement Config);
