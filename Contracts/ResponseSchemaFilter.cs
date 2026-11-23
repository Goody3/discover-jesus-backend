using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace DiscoverJesus.Api.Contracts;

public sealed class ResponseSchemaFilter : ISchemaFilter
{
    private static readonly HashSet<Type> ResponseTypes =
    [
        typeof(HealthResponse),
        typeof(PageSummary),
        typeof(PageResponse),
        typeof(ComponentResponse),
        typeof(BindingResponse),
        typeof(ActionResponse),
        typeof(ReusableComponentSummary),
        typeof(ReusableComponentDraftResponse),
        typeof(ReusableComponentResponse),
        typeof(PageContentDraftResponse),
        typeof(PageContentResponse),
        typeof(PublishedPageSummary),
        typeof(AuthorSessionResponse),
        typeof(AuthorPasskeyResponse)
    ];

    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        // These output-only DTOs always serialize every property, including false and null.
        // Do not change request validation or apply this rule to framework error responses.
        if (ResponseTypes.Contains(context.Type)
            || context.Type.IsGenericType
            && context.Type.GetGenericTypeDefinition() == typeof(PasskeyOptionsResponse<>))
        {
            schema.Required.UnionWith(schema.Properties.Keys);
        }
    }
}
