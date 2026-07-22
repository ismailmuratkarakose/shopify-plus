using System.Security.Claims;
using Marketplace.Bff.Mobile.Api.Cart;
using Marketplace.Bff.Mobile.Api.Clients;
using Marketplace.BuildingBlocks.MultiTenancy;

namespace Marketplace.Bff.Mobile.Api.Api;

/// <summary>
/// Mobil müşteri uygulaması için Backend-for-Frontend uçları. Downstream servisleri (Catalog,
/// Inventory, Order) çağırıp mobil ekranlara uygun tek DTO'da birleştirir; sepet durumunu Redis'te tutar.
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

    // --- Katalog: Catalog + Inventory birleşimi ---
    private static void MapCatalog(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/catalog").WithTags("Mobile.Catalog");

        group.MapGet("/", async (ICatalogApi catalog, IInventoryApi inventory,
            CancellationToken ct, int page = 1, int pageSize = 20) =>
        {
            var products = await catalog.GetProductsAsync(page, pageSize, ct);
            var stock = (await inventory.GetAllAsync(ct)).ToDictionary(i => i.ProductId, i => i.Available);

            var result = products.Select(p =>
            {
                var available = stock.GetValueOrDefault(p.Id, 0);
                return new MobileProductDto(p.Id, p.Sku, p.Title, p.Description, p.Price, p.Currency,
                    available, available > 0);
            });
            return Results.Ok(result);
        });

        group.MapGet("/{id:guid}", async (Guid id, ICatalogApi catalog, IInventoryApi inventory, CancellationToken ct) =>
        {
            var product = await catalog.GetProductAsync(id, ct);
            if (product is null)
                return Results.Problem($"Ürün bulunamadı: {id}", statusCode: StatusCodes.Status404NotFound, title: "product.not_found");

            var available = (await inventory.GetAllAsync(ct)).FirstOrDefault(i => i.ProductId == id)?.Available ?? 0;
            return Results.Ok(new MobileProductDto(product.Id, product.Sku, product.Title, product.Description,
                product.Price, product.Currency, available, available > 0));
        });
    }

    // --- Sepet (Redis) ---
    private static void MapCart(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/cart").WithTags("Mobile.Cart");

        group.MapGet("/", async (ClaimsPrincipal user, ITenantContext tenant, ICartStore store, CancellationToken ct) =>
        {
            var cart = await store.GetAsync(CartKey.For(user, tenant), ct);
            return Results.Ok(cart.ToView());
        });

        group.MapPost("/items", async (AddToCartRequest req, ClaimsPrincipal user, ITenantContext tenant,
            ICartStore store, ICatalogApi catalog, IInventoryApi inventory, CancellationToken ct) =>
        {
            if (req.Quantity <= 0)
                return Results.Problem("Adet 0'dan büyük olmalı.", statusCode: StatusCodes.Status400BadRequest, title: "cart.invalid_quantity");

            var product = await catalog.GetProductAsync(req.ProductId, ct);
            if (product is null)
                return Results.Problem($"Ürün bulunamadı: {req.ProductId}", statusCode: StatusCodes.Status404NotFound, title: "product.not_found");

            var key = CartKey.For(user, tenant);
            var cart = await store.GetAsync(key, ct);
            var line = cart.Lines.FirstOrDefault(l => l.ProductId == req.ProductId);
            var desiredQty = (line?.Quantity ?? 0) + req.Quantity;

            var available = (await inventory.GetAllAsync(ct)).FirstOrDefault(i => i.ProductId == req.ProductId)?.Available ?? 0;
            if (desiredQty > available)
                return Results.Problem($"Yetersiz stok. Mevcut: {available}", statusCode: StatusCodes.Status409Conflict, title: "cart.insufficient_stock");

            if (line is null)
            {
                cart.Lines.Add(new CartLine
                {
                    ProductId = product.Id,
                    Sku = product.Sku,
                    Title = product.Title,
                    UnitPrice = product.Price,
                    Currency = product.Currency,
                    Quantity = req.Quantity
                });
            }
            else
            {
                line.Quantity = desiredQty;
                // Fiyat/başlık anlık kopyayı güncel tut.
                line.UnitPrice = product.Price;
                line.Title = product.Title;
                line.Currency = product.Currency;
            }

            await store.SaveAsync(key, cart, ct);
            return Results.Ok(cart.ToView());
        });

        group.MapPut("/items/{productId:guid}", async (Guid productId, UpdateQtyRequest req, ClaimsPrincipal user,
            ITenantContext tenant, ICartStore store, IInventoryApi inventory, CancellationToken ct) =>
        {
            var key = CartKey.For(user, tenant);
            var cart = await store.GetAsync(key, ct);
            var line = cart.Lines.FirstOrDefault(l => l.ProductId == productId);
            if (line is null)
                return Results.Problem($"Sepette ürün yok: {productId}", statusCode: StatusCodes.Status404NotFound, title: "cart.item_not_found");

            if (req.Quantity <= 0)
            {
                cart.Lines.Remove(line);
            }
            else
            {
                var available = (await inventory.GetAllAsync(ct)).FirstOrDefault(i => i.ProductId == productId)?.Available ?? 0;
                if (req.Quantity > available)
                    return Results.Problem($"Yetersiz stok. Mevcut: {available}", statusCode: StatusCodes.Status409Conflict, title: "cart.insufficient_stock");
                line.Quantity = req.Quantity;
            }

            await store.SaveAsync(key, cart, ct);
            return Results.Ok(cart.ToView());
        });

        group.MapDelete("/items/{productId:guid}", async (Guid productId, ClaimsPrincipal user, ITenantContext tenant,
            ICartStore store, CancellationToken ct) =>
        {
            var key = CartKey.For(user, tenant);
            var cart = await store.GetAsync(key, ct);
            cart.Lines.RemoveAll(l => l.ProductId == productId);
            await store.SaveAsync(key, cart, ct);
            return Results.Ok(cart.ToView());
        });

        group.MapDelete("/", async (ClaimsPrincipal user, ITenantContext tenant, ICartStore store, CancellationToken ct) =>
        {
            await store.DeleteAsync(CartKey.For(user, tenant), ct);
            return Results.NoContent();
        });
    }

    // --- Checkout: sepetten sipariş oluştur ---
    private static void MapCheckout(RouteGroupBuilder api)
    {
        api.MapPost("/checkout", async (ClaimsPrincipal user, ITenantContext tenant,
            ICartStore store, IOrderApi orders, CancellationToken ct) =>
        {
            var key = CartKey.For(user, tenant);
            var cart = await store.GetAsync(key, ct);
            if (cart.Lines.Count == 0)
                return Results.Problem("Sepet boş.", statusCode: StatusCodes.Status400BadRequest, title: "checkout.empty_cart");

            var currency = cart.Lines[0].Currency;
            var body = new CreateOrderUpstream(currency,
                cart.Lines.Select(l => new CreateOrderItemUpstream(l.ProductId, l.Sku, l.Quantity, l.UnitPrice)).ToList());

            var order = await orders.CreateAsync(body, ct);
            if (order is null)
                return Results.Problem("Sipariş oluşturulamadı.", statusCode: StatusCodes.Status502BadGateway, title: "checkout.order_failed");

            // Sipariş oluştu → sepeti temizle. (Saga ödeme/stok akışını asenkron yürütür.)
            await store.DeleteAsync(key, ct);

            return Results.Ok(new CheckoutResponse(order.Id, order.Status, order.TotalAmount, order.Currency, order.StatusReason));
        }).WithTags("Mobile.Checkout");
    }

    // --- Sipariş takibi (Order proxy) ---
    private static void MapOrders(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/orders").WithTags("Mobile.Orders");

        group.MapGet("/", async (IOrderApi orders, CancellationToken ct, int page = 1, int pageSize = 20) =>
            Results.Ok(await orders.GetOrdersAsync(page, pageSize, ct)));

        group.MapGet("/{id:guid}", async (Guid id, IOrderApi orders, CancellationToken ct) =>
        {
            var order = await orders.GetOrderAsync(id, ct);
            return order is null
                ? Results.Problem($"Sipariş bulunamadı: {id}", statusCode: StatusCodes.Status404NotFound, title: "order.not_found")
                : Results.Ok(order);
        });
    }
}
