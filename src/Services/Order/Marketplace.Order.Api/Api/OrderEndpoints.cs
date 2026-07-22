using System.Security.Claims;
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

        // Satıcı kapsamı: merchant kendi siparişlerini (satışlarını) görür (tenant-filtered).
        group.MapGet("/", async (ISender sender, int page = 1, int pageSize = 20) =>
            (await sender.Send(new GetOrdersQuery(page, pageSize))).ToHttpResult());

        // Alıcı kapsamı: müşterinin farklı satıcılardaki tüm siparişleri (JWT sub ile).
        group.MapGet("/purchases", async (ClaimsPrincipal user, ISender sender, int page = 1, int pageSize = 20) =>
            (await sender.Send(new GetMyPurchasesQuery(BuyerOf(user), page, pageSize))).ToHttpResult());

        group.MapGet("/purchases/{id:guid}", async (Guid id, ClaimsPrincipal user, ISender sender) =>
            (await sender.Send(new GetMyPurchaseByIdQuery(BuyerOf(user), id))).ToHttpResult());

        group.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
            (await sender.Send(new GetOrderByIdQuery(id))).ToHttpResult());

        // Sipariş oluştur: alıcı JWT sub'dan; BFF checkout MerchantId=satıcı gönderir.
        group.MapPost("/", async (CreateOrderCommand cmd, ClaimsPrincipal user, ISender sender) =>
            (await sender.Send(cmd with { BuyerRef = BuyerOf(user) }))
                .ToHttpResult(created: true, location: r => $"/api/orders/{r.Id}"));

        return app;
    }

    private static string BuyerOf(ClaimsPrincipal user) =>
        user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
}
