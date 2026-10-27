using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiscoverJesus.Api.Contracts;

public sealed record PageRequest(
    [property: Required, JsonRequired] string Slug,
    [property: Required, JsonRequired] string Title,
    [property: JsonRequired] bool IsPublished);
public sealed record ComponentRequest(
    [property: Required, JsonRequired] string Key,
    [property: Required, JsonRequired] string Type,
    [property: JsonRequired] JsonElement Props,
    [property: JsonRequired] int SortOrder);
public sealed record BindingRequest(
    [property: Required, JsonRequired] string Name,
    [property: Required, JsonRequired] string Source,
    [property: JsonRequired] JsonElement Config);
public sealed record ActionRequest(
    [property: Required, JsonRequired] string Name,
    [property: Required, JsonRequired] string Type,
    [property: JsonRequired] JsonElement Config);

public sealed record PageSummary(int Id, string Slug, string Title, bool IsPublished);
public sealed record PageResponse(
    int Id,
    string Slug,
    string Title,
    bool IsPublished,
    IReadOnlyList<ComponentResponse> Components);
public sealed record ComponentResponse(
    int Id,
    string Key,
    string Type,
    JsonElement Props,
    int SortOrder,
    IReadOnlyList<BindingResponse> Bindings,
    IReadOnlyList<ActionResponse> Actions);
public sealed record BindingResponse(int Id, string Name, string Source, JsonElement Config);
public sealed record ActionResponse(int Id, string Name, string Type, JsonElement Config);
