using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace DiscoverJesus.Api.Contracts;

public sealed class RequestSchemaFilter : ISchemaFilter
{
    private const string NonWhitespacePattern = "\\S";

    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        switch (context.Type.Name)
        {
            case nameof(PageRequest):
                SetString(schema, "slug", 1, 200, "^[a-z0-9]+(?:-[a-z0-9]+)*$");
                SetString(schema, "title", 1, 300, NonWhitespacePattern);
                break;
            case nameof(ComponentRequest):
                SetString(schema, "key", 1, 200, NonWhitespacePattern);
                SetString(schema, "type", 1, 100, allowedValues: ContentTypeRules.ManagementComponentTypes);
                SetObject(schema, "props");
                break;
            case nameof(BindingRequest):
                SetString(schema, "name", 1, 200, NonWhitespacePattern);
                SetString(schema, "source", 1, 500, NonWhitespacePattern);
                SetObject(schema, "config");
                break;
            case nameof(ActionRequest):
                SetString(schema, "name", 1, 200, NonWhitespacePattern);
                SetString(schema, "type", 1, 100, NonWhitespacePattern);
                SetObject(schema, "config");
                break;
            case nameof(CreateReusableComponentRequest):
                SetString(schema, "name", 1, 200, NonWhitespacePattern);
                SetString(schema, "locale", 1, 35, "^[A-Za-z]{2,8}(?:-[A-Za-z0-9]{1,8})*$");
                SetObject(schema, "content");
                break;
            case nameof(UpdateReusableComponentNameRequest):
                SetString(schema, "name", 1, 200, NonWhitespacePattern);
                break;
            case nameof(UpdateReusableComponentDraftRequest):
                SetObject(schema, "content");
                break;
            case nameof(UpdatePageContentDraftRequest):
                SetArray(schema, "sections");
                break;
        }
    }

    private static void SetString(
        OpenApiSchema schema,
        string propertyName,
        int minLength,
        int maxLength,
        string? pattern = null,
        IEnumerable<string>? allowedValues = null)
    {
        var property = GetProperty(schema, propertyName);
        property.MinLength = minLength;
        property.MaxLength = maxLength;
        if (pattern is not null)
        {
            property.Pattern = pattern;
        }
        if (allowedValues is not null)
        {
            property.Enum = allowedValues.Select(value => (IOpenApiAny)new OpenApiString(value)).ToList();
        }
    }

    private static void SetObject(OpenApiSchema schema, string propertyName)
    {
        var property = GetProperty(schema, propertyName);
        property.Type = "object";
        property.AdditionalProperties = new OpenApiSchema();
    }

    private static void SetArray(OpenApiSchema schema, string propertyName)
    {
        var property = GetProperty(schema, propertyName);
        property.Type = "array";
        property.Items = new OpenApiSchema
        {
            Type = "object",
            AdditionalProperties = new OpenApiSchema()
        };
    }

    private static OpenApiSchema GetProperty(OpenApiSchema schema, string propertyName) =>
        schema.Properties.TryGetValue(propertyName, out var property)
            ? property
            : throw new InvalidOperationException(
                $"The OpenAPI schema for {schema.Title ?? "request"} has no {propertyName} property.");
}
