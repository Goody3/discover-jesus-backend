using DiscoverJesus.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace DiscoverJesus.Api.Contracts;

public sealed class AuthorSecurityOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any()
            || !metadata.OfType<IAuthorizeData>().Any())
        {
            return;
        }

        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = AuthorSessionDefaults.Scheme
                }
            }] = []
        });

        if (metadata.OfType<CsrfProtectedEndpointMetadata>().Any()
            && !string.Equals(context.ApiDescription.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(context.ApiDescription.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(context.ApiDescription.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "X-CSRF-Token",
                In = ParameterLocation.Header,
                Required = true,
                Description = "Session-bound CSRF token returned by GET /api/auth/session.",
                Schema = new OpenApiSchema { Type = "string" }
            });
        }
    }
}
