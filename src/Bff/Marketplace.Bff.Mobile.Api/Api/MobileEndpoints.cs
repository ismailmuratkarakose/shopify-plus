using System.Security.Claims;
using Marketplace.Bff.Mobile.Api.Cart;
using Marketplace.Bff.Mobile.Api.Clients;
using Marketplace.BuildingBlocks.MultiTenancy;

namespace Marketplace.Bff.Mobile.Api.Api;

/// <summary>
/// Mobil müşteri BFF'i. Katalog = ürün master + satıcı kıyası (Catalog); sepet Redis'te teklif (offer)
/// bazlı; checkout sepeti satıcıya göre böler → merchant başına bir sipariş (Order). Stok, ekleme anında
/// değil checkout saga'sında (Order↔Inventory) zorlanır — satıcının stoğu yetmezse o sipariş reddedilir.
/// </summary>
public static class MobileEndpoints
{
    public static IEndpointRouteBuilder MapMobileEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/mobile").RequireAuthorization();
        MapCatalog(api);
        MapCart(api);
        MapCheckout(api);
        MapOrders(api);
        return app;
    }

    // --- Katalog: master + satıcı kıyası ---
    private static void MapCatalog(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/catalog").WithTags("Mobile.Catalog");

        group.MapGet("/", async (ICatalogApi catalog, CancellationToken ct,
            int page = 1, int pageSize = 20, string? search = null) =>
        {
            var products = await catalog.GetProductsAsync(page, pageSize, search, ct);
            var result = products.Select(p => new MobileProductDto(p.Id, p.Barcode, p.Title, p.Brand, p.OfferCount, p.MinPrice));
            return Results.Ok(result);
        });

        group.MapGet("/{id:guid}", async (Guid id, ICatalogApi catalog, CancellationToken ct) =>
        {
            var p = await catalog.GetProductAsync(id, ct);
            if (p is null)
                return Results.Problem($"Ürün bulunamadı: {id}", statusCode: StatusCodes.Status404NotFound, title: "product.not_found");

            var sellers = p.Offers.Select(o => new SellerDto(o.OfferId, o.MerchantId, o.Sku, o.Price, o.Currency)).ToList();
            return Results.Ok(new MobileProductDetailDto(
                p.Product.Id, p.Product.Barcode, p.Product.Title, p.Product.Description, p.Product.Brand, p.Product.ImageUrl, sellers));
        });
    }

    // --- Sepet (Redis, teklif bazlı) ---
    private static void MapCart(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/cart").WithTags("Mobile.Cart");

        group.MapGet("/", async (ClaimsPrincipal user, ITenantContext tenant, ICartStore store, CancellationToken ct) =>
        {
            var cart = await store.GetAsync(CartKey.For(user, tenant), ct);
            return Results.Ok(cart.ToView());
        });

        group.MapPost("/items", async (AddToCartRequest req, ClaimsPrincipal user, ITenantContext tenant,
            ICartStore store, ICatalogApi catalog, CancellationToken ct) =>
        {
            if (req.Quantity <= 0)
                return Results.Problem("Adet 0'dan büyük olmalı.", statusCode: StatusCodes.Status400BadRequest, title: "cart.invalid_quantity");

            var product = await catalog.GetProductAsync(req.ProductId, ct);
            if (product is null)
                return Results.Problem($"Ürün bulunamadı: {req.ProductId}", statusCode: StatusCodes.Status404NotFound, title: "product.not_found");

            var seller = product.Offers.FirstOrDefault(o => o.MerchantId == req.MerchantId && o.IsActive);
            if (seller is null)
                return Results.Problem($"Bu ürün için satıcı teklifi yok: {req.MerchantId}", statusCode: StatusCodes.Status404NotFound, title: "offer.not_found");

            var key = CartKey.For(user, tenant);
            var cart = await store.GetAsync(key, ct);
            var line = cart.Lines.FirstOrDefault(l => l.OfferId == seller.OfferId);
            if (line is null)
            {
                cart.Lines.Add(new CartLine
                {
                    OfferId = seller.OfferId,
                    ProductId = product.Product.Id,
                    MerchantId = seller.MerchantId,
                    Barcode = product.Product.Barcode,
                    Sku = seller.Sku ?? product.Product.Barcode,
                    Title = product.Product.Title,
                    UnitPrice = seller.Price,
                    Currency = seller.Currency,
                    Quantity = req.Quantity
                });
            }
            else
            {
                line.Quantity += req.Quantity;
                line.UnitPrice = seller.Price; // fiyatı güncel tut
            }

            await store.SaveAsync(key, cart, ct);
            return Results.Ok(cart.ToView());
        });

        group.MapPut("/items/{offerId:guid}", async (Guid offerId, UpdateQtyRequest req, ClaimsPrincipal user,
            ITenantContext tenant, ICartStore store, CancellationToken ct) =>
        {
            var key = CartKey.For(user, tenant);
            var cart = await store.GetAsync(key, ct);
            var line = cart.Lines.FirstOrDefault(l => l.OfferId == offerId);
            if (line is null)
                return Results.Problem($"Sepette teklif yok: {offerId}", statusCode: StatusCodes.Status404NotFound, title: "cart.item_not_found");

            if (req.Quantity <= 0) cart.Lines.Remove(line);
            else line.Quantity = req.Quantity;

            await store.SaveAsync(key, cart, ct);
            return Results.Ok(cart.ToView());
        });

        group.MapDelete("/items/{offerId:guid}", async (Guid offerId, ClaimsPrincipal user, ITenantContext tenant,
            ICartStore store, CancellationToken ct) =>
        {
            var key = CartKey.For(user, tenant);
            var cart = await store.GetAsync(key, ct);
            cart.Lines.RemoveAll(l => l.OfferId == offerId);
            await store.SaveAsync(key, cart, ct);
            return Results.Ok(cart.ToView());
        });

        group.MapDelete("/", async (ClaimsPrincipal user, ITenantContext tenant, ICartStore store, CancellationToken ct) =>
        {
            await store.DeleteAsync(CartKey.For(user, tenant), ct);
            return Results.NoContent();
        });
    }

    // --- Checkout: sepeti satıcıya göre böl → merchant başına bir sipariş ---
    private static void MapCheckout(RouteGroupBuilder api)
    {
        api.MapPost("/checkout", async (ClaimsPrincipal user, ITenantContext tenant,
            ICartStore store, IOrderApi orders, CancellationToken ct) =>
        {
            var key = CartKey.For(user, tenant);
            var cart = await store.GetAsync(key, ct);
            if (cart.Lines.Count == 0)
                return Results.Problem("Sepet boş.", statusCode: StatusCodes.Status400BadRequest, title: "checkout.empty_cart");

            var created = new List<CheckoutOrderDto>();
            foreach (var group in cart.Lines.GroupBy(l => l.MerchantId))
            {
                var currency = group.First().Currency;
                var body = new CreateOrderUpstream(
                    currency,
                    group.Select(l => new CreateOrderItemUpstream(l.ProductId, l.Sku, l.Quantity, l.UnitPrice)).ToList(),
                    group.Key);

                var order = await orders.CreateAsync(body, ct);
                if (order is not null)
                    created.Add(new CheckoutOrderDto(order.Id, order.MerchantId, order.Status, order.TotalAmount, order.Currency, order.StatusReason));
            }

            if (created.Count == 0)
                return Results.Problem("Sipariş oluşturulamadı.", statusCode: StatusCodes.Status502BadGateway, title: "checkout.order_failed");

            await store.DeleteAsync(key, ct); // siparişler oluştu → sepeti temizle
            return Results.Ok(new CheckoutResultDto(created));
        }).WithTags("Mobile.Checkout");
    }

    // --- Sipariş takibi: alıcının farklı satıcılardaki siparişleri ---
    private static void MapOrders(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/orders").WithTags("Mobile.Orders");

        group.MapGet("/", async (IOrderApi orders, CancellationToken ct, int page = 1, int pageSize = 20) =>
            Results.Ok(await orders.GetMyPurchasesAsync(page, pageSize, ct)));

        group.MapGet("/{id:guid}", async (Guid id, IOrderApi orders, CancellationToken ct) =>
        {
            var order = await orders.GetMyPurchaseAsync(id, ct);
            return order is null
                ? Results.Problem($"Sipariş bulunamadı: {id}", statusCode: StatusCodes.Status404NotFound, title: "order.not_found")
                : Results.Ok(order);
        });
    }
}
