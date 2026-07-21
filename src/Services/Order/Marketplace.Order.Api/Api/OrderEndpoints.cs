using Marketplace.BuildingBlocks.Web;
using Marketplace.Order.Api.Application;
using MediatR;

namespace Marketplace.Order.Api.Api;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders")
            .RequireAuthorization()
            .WithTags("Orders");

        group.MapGet("/", async (ISender sender, int page = 1, int pageSize = 20) =>
            (await sender.Send(new GetOrdersQuery(page, pageSize))).ToHttpResult());

        group.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
            (await sender.Send(new GetOrderByIdQuery(id))).ToHttpResult());

        group.MapPost("/", async (CreateOrderCommand cmd, ISender sender) =>
            (await sender.Send(cmd)).ToHttpResult(created: true, location: r => $"/api/orders/{r.Id}"));

        return app;
    }
}
