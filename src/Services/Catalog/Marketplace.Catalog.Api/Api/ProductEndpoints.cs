using Marketplace.BuildingBlocks.Web;
using Marketplace.Catalog.Api.Application;
using MediatR;

namespace Marketplace.Catalog.Api.Api;

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/products")
            .RequireAuthorization()
            .WithTags("Products");

        group.MapGet("/", async (ISender sender, int page = 1, int pageSize = 20) =>
        {
            var result = await sender.Send(new GetProductsQuery(page, pageSize));
            return result.ToHttpResult();
        });

        group.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetProductByIdQuery(id));
            return result.ToHttpResult();
        });

        group.MapPost("/", async (CreateProductCommand cmd, ISender sender) =>
        {
            var result = await sender.Send(cmd);
            return result.ToHttpResult(created: true, location: r => $"/api/products/{r.Id}");
        });

        return app;
    }
}
