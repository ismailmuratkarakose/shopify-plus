using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.ShopifySync.Api.Infrastructure;
using Marketplace.ShopifySync.Api.Shopify;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.ShopifySync.Api.Api;

public record SyncedVariantDto(long VariantId, string? Sku, string? Barcode, decimal Price, decimal? CompareAtPrice, int InventoryQuantity, string? Title);
public record SyncedProductDto(long ProductId, string Title, string? Description, string? Vendor, string? ProductType, string Handle, string Status, string? ImageUrl, IReadOnlyList<SyncedVariantDto> Variants);
public record SyncedCollectionDto(long CollectionId, string Title, string Handle, IReadOnlyList<long> ProductIds);
public record SyncedOrderLineDto(long ProductId, long VariantId, string? Sku, string Title, int Quantity, decimal Price);
public record SyncedOrderDto(long OrderId, string Name, long? CustomerId, string? Email, string FinancialStatus, string? FulfillmentStatus, decimal TotalPrice, string Currency, DateTimeOffset CreatedAt, IReadOnlyList<SyncedOrderLineDto> Lines);
public record SyncedCustomerDto(long CustomerId, string? Email, string? FirstName, string? LastName, string? Phone, int OrdersCount, decimal TotalSpent);

/// <summary>
/// Store Data uçları: Shopify'dan senkronlanan ürün/koleksiyon read-model'ini okur ve senkronu tetikler.
/// (Mobil uygulama ve kişiselleştirme motoru bu read-model'i tüketir.)
/// </summary>
public static class StoreDataEndpoints
{
    public static IEndpointRouteBuilder MapStoreDataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/shopify").RequireAuthorization().WithTags("StoreData");

        // Senkronu tetikle (Faz B: inline; ileride arka plan job + webhook incremental).
        group.MapPost("/sync", async (ITenantContext tenant, StoreSyncService sync, CancellationToken ct) =>
        {
            if (tenant.TenantId is not { } merchantId)
                return Results.Problem("Mağaza kapsamı yok.", statusCode: StatusCodes.Status401Unauthorized, title: "tenant.missing");
            try
            {
                var (products, collections, orders, customers) = await sync.SyncAsync(merchantId, ct);
                return Results.Ok(new { synced = true, products, collections, orders, customers });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict, title: "sync.no_integration");
            }
        });

        group.MapGet("/products", async (ShopifySyncDbContext db, CancellationToken ct,
            int page = 1, int pageSize = 20, string? search = null) =>
        {
            var p = Math.Max(1, page);
            var size = Math.Clamp(pageSize, 1, 100);
            var q = db.SyncedProducts.Include(x => x.Variants).AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
                q = q.Where(x => x.Title.Contains(search));

            var items = await q.OrderByDescending(x => x.ShopifyUpdatedAt)
                .Skip((p - 1) * size).Take(size).ToListAsync(ct);
            return Results.Ok(items.Select(ToDto));
        });

        group.MapGet("/products/{shopifyProductId:long}", async (long shopifyProductId, ShopifySyncDbContext db, CancellationToken ct) =>
        {
            var x = await db.SyncedProducts.Include(p => p.Variants)
                .FirstOrDefaultAsync(p => p.ShopifyProductId == shopifyProductId, ct);
            return x is null
                ? Results.Problem($"Ürün bulunamadı: {shopifyProductId}", statusCode: StatusCodes.Status404NotFound, title: "product.not_found")
                : Results.Ok(ToDto(x));
        });

        group.MapGet("/collections", async (ShopifySyncDbContext db, CancellationToken ct) =>
        {
            var cols = await db.SyncedCollections.Include(c => c.Products).OrderBy(c => c.Title).ToListAsync(ct);
            return Results.Ok(cols.Select(c => new SyncedCollectionDto(
                c.ShopifyCollectionId, c.Title, c.Handle, c.Products.Select(p => p.ShopifyProductId).ToList())));
        });

        // Siparişler: Shopify'da oluşur (checkout orada), burada SALT OKUNUR.
        group.MapGet("/orders", async (ShopifySyncDbContext db, CancellationToken ct,
            int page = 1, int pageSize = 20, long? customerId = null, string? financialStatus = null) =>
        {
            var p = Math.Max(1, page);
            var size = Math.Clamp(pageSize, 1, 100);
            var q = db.SyncedOrders.Include(o => o.Lines).AsQueryable();
            if (customerId is { } cid) q = q.Where(o => o.ShopifyCustomerId == cid);
            if (!string.IsNullOrWhiteSpace(financialStatus)) q = q.Where(o => o.FinancialStatus == financialStatus);

            var items = await q.OrderByDescending(o => o.ShopifyCreatedAt)
                .Skip((p - 1) * size).Take(size).ToListAsync(ct);
            return Results.Ok(items.Select(ToDto));
        });

        group.MapGet("/orders/{shopifyOrderId:long}", async (long shopifyOrderId, ShopifySyncDbContext db, CancellationToken ct) =>
        {
            var o = await db.SyncedOrders.Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.ShopifyOrderId == shopifyOrderId, ct);
            return o is null
                ? Results.Problem($"Sipariş bulunamadı: {shopifyOrderId}", statusCode: StatusCodes.Status404NotFound, title: "order.not_found")
                : Results.Ok(ToDto(o));
        });

        group.MapGet("/customers", async (ShopifySyncDbContext db, CancellationToken ct,
            int page = 1, int pageSize = 20, string? search = null) =>
        {
            var p = Math.Max(1, page);
            var size = Math.Clamp(pageSize, 1, 100);
            var q = db.SyncedCustomers.AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
                q = q.Where(c => (c.Email != null && c.Email.Contains(search))
                              || (c.FirstName != null && c.FirstName.Contains(search))
                              || (c.LastName != null && c.LastName.Contains(search)));

            var items = await q.OrderByDescending(c => c.TotalSpent)
                .Skip((p - 1) * size).Take(size).ToListAsync(ct);
            return Results.Ok(items.Select(c => new SyncedCustomerDto(
                c.ShopifyCustomerId, c.Email, c.FirstName, c.LastName, c.Phone, c.OrdersCount, c.TotalSpent)));
        });

        return app;
    }

    private static SyncedProductDto ToDto(Domain.SyncedProduct x) => new(
        x.ShopifyProductId, x.Title, x.Description, x.Vendor, x.ProductType, x.Handle, x.Status, x.ImageUrl,
        x.Variants.Select(v => new SyncedVariantDto(v.ShopifyVariantId, v.Sku, v.Barcode, v.Price, v.CompareAtPrice, v.InventoryQuantity, v.Title)).ToList());

    private static SyncedOrderDto ToDto(Domain.SyncedOrder o) => new(
        o.ShopifyOrderId, o.Name, o.ShopifyCustomerId, o.Email, o.FinancialStatus, o.FulfillmentStatus,
        o.TotalPrice, o.Currency, o.ShopifyCreatedAt,
        o.Lines.Select(l => new SyncedOrderLineDto(l.ShopifyProductId, l.ShopifyVariantId, l.Sku, l.Title, l.Quantity, l.Price)).ToList());
}
