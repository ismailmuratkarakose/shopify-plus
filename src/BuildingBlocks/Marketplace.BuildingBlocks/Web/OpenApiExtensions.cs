using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Marketplace.BuildingBlocks.Web;

/// <summary>
/// Tüm servislerde ortak Swagger UI + built-in OpenAPI kurulumu.
/// Korumalı endpoint'leri denemek için Bearer (JWT) güvenlik şeması eklenir.
/// </summary>
public static class OpenApiExtensions
{
    public static IServiceCollection AddMarketplaceOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
        });
        return services;
    }

    public static WebApplication UseMarketplaceSwaggerUi(this WebApplication app, string title)
    {
        // Built-in OpenAPI dokümanı: /openapi/v1.json
        app.MapOpenApi();
        // Swagger UI: /swagger
        app.UseSwaggerUI(o =>
        {
            o.SwaggerEndpoint("/openapi/v1.json", title);
            o.DocumentTitle = title;
        });
        return app;
    }
}

public sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Keycloak JWT access token. Sadece token'ı yapıştırın ('Bearer ' öneki gerekmez)."
        };

        document.Security ??= new List<OpenApiSecurityRequirement>();
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
        });

        return Task.CompletedTask;
    }
}
