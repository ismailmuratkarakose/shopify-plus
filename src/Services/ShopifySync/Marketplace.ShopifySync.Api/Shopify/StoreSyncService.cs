using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Security;
using Marketplace.ShopifySync.Api.Domain;
using Marketplace.ShopifySync.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.ShopifySync.Api.Shopify;

/// <summary>
/// Bir mağazanın Shopify verisini (ürün + koleksiyon) çekip read-model'e tam yeniler (full refresh).
/// Shopify kaynak sistemdir. Faz B: pull-based; ileride webhook ile incremental + zamanlı reconciliation.
/// </summary>
public sealed class StoreSyncService
{
    private readonly ShopifySyncDbContext _db;
    private readonly IShopifyClient _shopify;
    private readonly ISecretProtector _protector;
    private readonly ITenantContext _tenant;
    private readonly ILogger<StoreSyncService> _logger;

    public StoreSyncService(
        ShopifySyncDbContext db, IShopifyClient shopify, ISecretProtector protector,
        ITenantContext tenant, ILogger<StoreSyncService> logger)
    {
        _db = db;
        _shopify = shopify;
        _protector = protector;
        _tenant = tenant;
        _logger = logger;
    }

    public async Task<(int Products, int Collections)> SyncAsync(Guid merchantId, CancellationToken ct)
    {
        _tenant.SetTenant(merchantId, isPlatformScope: false);

        var integration = await _db.Integrations.FirstOrDefaultAsync(i => i.TenantId == merchantId, ct);
        if (integration is null || string.IsNullOrEmpty(integration.EncryptedAccessToken))
            throw new InvalidOperationException("Mağaza Shopify entegrasyonu yok — önce bağlayın (/api/shopify/connect).");

        var creds = new ShopifyStoreCredentials(integration.ShopDomain, _protector.Unprotect(integration.EncryptedAccessToken));

        var products = await _shopify.GetProductsAsync(creds, ct);
        var collections = await _shopify.GetCollectionsAsync(creds, ct);

        // Full refresh: eski kayıtları sil (cascade variant/üyelik), sonra yeniden ekle. İki SaveChanges:
        // unique (tenant, shopifyId) index'i silme+ekleme aynı transaction'da çakışmasın.
        _db.SyncedProducts.RemoveRange(await _db.SyncedProducts.Include(p => p.Variants).ToListAsync(ct));
        _db.SyncedCollections.RemoveRange(await _db.SyncedCollections.Include(c => c.Products).ToListAsync(ct));
        await _db.SaveChangesAsync(ct);

        foreach (var p in products)
        {
            _db.SyncedProducts.Add(new SyncedProduct
            {
                ShopifyProductId = p.ProductId,
                Title = p.Title,
                Description = p.Description,
                Vendor = p.Vendor,
                ProductType = p.ProductType,
                Handle = p.Handle,
                Status = p.Status,
                ImageUrl = p.ImageUrl,
                ShopifyUpdatedAt = p.UpdatedAt,
                Variants = p.Variants.Select(v => new SyncedVariant
                {
                    ShopifyVariantId = v.VariantId,
                    Sku = v.Sku,
                    Barcode = v.Barcode,
                    Price = v.Price,
                    CompareAtPrice = v.CompareAtPrice,
                    InventoryQuantity = v.InventoryQuantity,
                    Title = v.Title
                }).ToList()
            });
        }

        foreach (var c in collections)
        {
            _db.SyncedCollections.Add(new SyncedCollection
            {
                ShopifyCollectionId = c.CollectionId,
                Title = c.Title,
                Handle = c.Handle,
                Products = c.ProductIds.Select(pid => new SyncedCollectionProduct { ShopifyProductId = pid }).ToList()
            });
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Mağaza senkronu: store={Store} ürün={P} koleksiyon={C}",
            integration.ShopDomain, products.Count, collections.Count);
        return (products.Count, collections.Count);
    }
}
