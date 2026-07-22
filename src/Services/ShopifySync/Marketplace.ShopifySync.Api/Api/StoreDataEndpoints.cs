using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.ShopifySync.Api.Infrastructure;
using Marketplace.ShopifySync.Api.Shopify;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.ShopifySync.Api.Api;

public record SyncedVariantDto(long VariantId, string? Sku, string? Barcode, decimal Price, decimal? CompareAtPrice, int InventoryQuantity, string? Title);
public record SyncedProductDto(long ProductId, string Title, string? Description, string? Vendor, string? ProductType, string Handle, string Status, string? ImageUrl, IReadOnlyList<SyncedVariantDto> Variants);
public record SyncedCollectionDto(long CollectionId, string Title, string Handle, IReadOnlyList<long> ProductIds);

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
                var (products, collections) = await sync.SyncAsync(merchantId, ct);
                return Results.Ok(new { synced = true, products, collections });
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

        return app;
    }

    private static SyncedProductDto ToDto(Domain.SyncedProduct x) => new(
        x.ShopifyProductId, x.Title, x.Description, x.Vendor, x.ProductType, x.Handle, x.Status, x.ImageUrl,
        x.Variants.Select(v => new SyncedVariantDto(v.ShopifyVariantId, v.Sku, v.Barcode, v.Price, v.CompareAtPrice, v.InventoryQuantity, v.Title)).ToList());
}
