using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Taxi.Web.Api.OpenApi;

/// <summary>
/// Ajoute le schéma de sécurité Bearer (JWT) au document OpenAPI/Scalar.
/// </summary>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        // 1. Register the Bearer scheme in components
        document.Components ??= new OpenApiComponents();
        if (document.Components.SecuritySchemes is null)
        {
            document.Components.SecuritySchemes =
                new Dictionary<string, IOpenApiSecurityScheme>();
        }

        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Coller le JWT (sans le préfixe 'Bearer')."
        };

        // 2. Add a global security requirement referencing the Bearer scheme.
        // In Microsoft.OpenApi v2.x the key type of OpenApiSecurityRequirement is
        // OpenApiSecuritySchemeReference (not OpenApiSecurityScheme).
        var requirement = new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = []
        };

        document.Security ??= [];
        document.Security.Add(requirement);

        return Task.CompletedTask;
    }
}
