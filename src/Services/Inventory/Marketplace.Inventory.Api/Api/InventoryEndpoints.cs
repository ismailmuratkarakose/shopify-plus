using Marketplace.BuildingBlocks.Web;
using Marketplace.Inventory.Api.Application;
using MediatR;

namespace Marketplace.Inventory.Api.Api;

public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/inventory")
            .RequireAuthorization()
            .WithTags("Inventory");

        group.MapGet("/", async (ISender sender) =>
            (await sender.Send(new GetInventoryQuery())).ToHttpResult());

        group.MapPost("/{productId:guid}/adjust", async (Guid productId, AdjustStockBody body, ISender sender) =>
            (await sender.Send(new AdjustStockCommand(productId, body.QuantityOnHand))).ToHttpResult());

        return app;
    }
}

public record AdjustStockBody(int QuantityOnHand);
