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
public record SyncedDiscountDto(long DiscountId, string Title, string Code, string DiscountType, decimal Value, string? Currency, DateTimeOffset StartsAt, DateTimeOffset? EndsAt, string Status, int UsageCount);
public record SyncedPageDto(long PageId, string Title, string Handle, string? BodyHtml, string Status, DateTimeOffset UpdatedAt);

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
        group.MapPost("/sync", async (IStoreContext scope, StoreSyncService sync, CancellationToken ct) =>
        {
            if (scope.StoreId is not { } merchantId)
                return Results.Problem("Mağaza kapsamı yok.", statusCode: StatusCodes.Status401Unauthorized, title: "store.missing");
            try
            {
                var r = await sync.SyncAsync(merchantId, "manual", ct);
                return Results.Ok(new
                {
                    synced = true,
                    products = r.Products,
                    collections = r.Collections,
                    orders = r.Orders,
                    customers = r.Customers,
                    discounts = r.Discounts,
                    pages = r.Pages,
                    durationMs = r.DurationMs
                });
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

        // İndirimler / kampanyalar (kampanya alanlarına bağlanmak üzere).
        group.MapGet("/discounts", async (ShopifySyncDbContext db, CancellationToken ct, string? status = null) =>
        {
            var q = db.SyncedDiscounts.AsQueryable();
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(d => d.Status == status);
            var items = await q.OrderByDescending(d => d.StartsAt).ToListAsync(ct);
            return Results.Ok(items.Select(d => new SyncedDiscountDto(
                d.ShopifyDiscountId, d.Title, d.Code, d.DiscountType, d.Value, d.Currency,
                d.StartsAt, d.EndsAt, d.Status, d.UsageCount)));
        });

        // Mağaza içerik sayfaları.
        group.MapGet("/pages", async (ShopifySyncDbContext db, CancellationToken ct) =>
        {
            var items = await db.SyncedPages.OrderBy(p => p.Title).ToListAsync(ct);
            return Results.Ok(items.Select(p => new SyncedPageDto(
                p.ShopifyPageId, p.Title, p.Handle, p.BodyHtml, p.Status, p.ShopifyUpdatedAt)));
        });

        group.MapGet("/pages/{handle}", async (string handle, ShopifySyncDbContext db, CancellationToken ct) =>
        {
            var p = await db.SyncedPages.FirstOrDefaultAsync(x => x.Handle == handle, ct);
            return p is null
                ? Results.Problem($"Sayfa bulunamadı: {handle}", statusCode: StatusCodes.Status404NotFound, title: "page.not_found")
                : Results.Ok(new SyncedPageDto(p.ShopifyPageId, p.Title, p.Handle, p.BodyHtml, p.Status, p.ShopifyUpdatedAt));
        });

        // Senkron durumu: veriler ne kadar güncel, son çalışma sonucu ne?
        group.MapGet("/sync/status", async (ShopifySyncDbContext db, CancellationToken ct) =>
        {
            var s = await db.SyncStates.FirstOrDefaultAsync(ct);
            if (s is null)
                return Results.Ok(new { lastStatus = "never", message = "Bu mağaza için henüz senkron çalıştırılmadı." });

            return Results.Ok(new
            {
                lastStatus = s.LastStatus,
                lastSyncAt = s.LastSyncAt,
                lastTrigger = s.LastTrigger,
                durationMs = s.DurationMs,
                lastError = s.LastError,
                counts = new
                {
                    products = s.ProductCount,
                    collections = s.CollectionCount,
                    orders = s.OrderCount,
                    customers = s.CustomerCount,
                    discounts = s.DiscountCount,
                    pages = s.PageCount
                }
            });
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
