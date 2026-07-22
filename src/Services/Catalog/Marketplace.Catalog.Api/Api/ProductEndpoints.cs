using Marketplace.BuildingBlocks.Web;
using Marketplace.Catalog.Api.Application;
using MediatR;

namespace Marketplace.Catalog.Api.Api;

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        // --- Merchant teklifleri (kendi fiyat/stok) ---
        var offers = app.MapGroup("/api/offers").RequireAuthorization().WithTags("Offers");

        offers.MapPost("/", async (CreateOfferCommand cmd, ISender sender) =>
            (await sender.Send(cmd)).ToHttpResult(created: true, location: r => $"/api/offers/{r.Id}"));

        offers.MapGet("/", async (ISender sender, int page = 1, int pageSize = 20) =>
            (await sender.Send(new GetMyOffersQuery(page, pageSize))).ToHttpResult());

        offers.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
            (await sender.Send(new GetOfferByIdQuery(id))).ToHttpResult());

        offers.MapPut("/{id:guid}", async (Guid id, UpdateOfferBody body, ISender sender) =>
            (await sender.Send(new UpdateOfferCommand(id, body.Price, body.IsActive))).ToHttpResult());

        // --- Global ürün kataloğu (master + satıcı kıyası) ---
        var products = app.MapGroup("/api/products").RequireAuthorization().WithTags("Products");

        products.MapGet("/", async (ISender sender, int page = 1, int pageSize = 20, string? search = null) =>
            (await sender.Send(new GetProductsQuery(page, pageSize, search))).ToHttpResult());

        products.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
            (await sender.Send(new GetProductByIdQuery(id))).ToHttpResult());

        products.MapGet("/by-barcode/{barcode}", async (string barcode, ISender sender) =>
            (await sender.Send(new GetProductByBarcodeQuery(barcode))).ToHttpResult());

        return app;
    }
}

public record UpdateOfferBody(decimal Price, bool IsActive);
