using Microsoft.AspNetCore.OpenApi;
// Microsoft.OpenApi 2.x (el que trae .NET 10) fusiono el namespace Models en la raiz:
// OpenApiDocument, OpenApiComponents y OpenApiSecurityScheme viven directamente aca.
using Microsoft.OpenApi;

namespace MusicReviews.Api.Infrastructure;

/// <summary>
/// Agrega el esquema de seguridad Bearer al documento OpenAPI para que Scalar
/// muestre el boton de autenticacion y mande el header Authorization.
/// </summary>
public sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    private const string SchemeName = "Bearer";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Pega el access token devuelto por /api/auth/login (sin el prefijo 'Bearer')."
        };

        return Task.CompletedTask;
    }
}
