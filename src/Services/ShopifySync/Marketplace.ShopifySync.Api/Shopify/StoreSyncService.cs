using System.Diagnostics;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Security;
using Marketplace.ShopifySync.Api.Domain;
using Marketplace.ShopifySync.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.ShopifySync.Api.Shopify;

public record StoreSyncResult(
    int Products, int Collections, int Orders, int Customers, int Discounts, int Pages, long DurationMs);

/// <summary>
/// Bir mağazanın Shopify verisini çekip read-model'e tam yeniler (full refresh) ve senkron durumunu kaydeder.
/// Shopify kaynak sistemdir. Tetikleyici: kullanıcı isteği (manual) veya periyodik mutabakat (reconciliation).
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

    public async Task<StoreSyncResult> SyncAsync(Guid merchantId, string trigger, CancellationToken ct)
    {
        _tenant.SetTenant(merchantId, isPlatformScope: false);
        var sw = Stopwatch.StartNew();

        try
        {
            var integration = await _db.Integrations.FirstOrDefaultAsync(i => i.TenantId == merchantId, ct);
            if (integration is null || string.IsNullOrEmpty(integration.EncryptedAccessToken))
                throw new InvalidOperationException("Mağaza Shopify entegrasyonu yok — önce bağlayın (/api/shopify/connect).");

            var creds = new ShopifyStoreCredentials(
                integration.ShopDomain, _protector.Unprotect(integration.EncryptedAccessToken));

            var products = await _shopify.GetProductsAsync(creds, ct);
            var collections = await _shopify.GetCollectionsAsync(creds, ct);
            var orders = await _shopify.GetOrdersAsync(creds, ct);
            var customers = await _shopify.GetCustomersAsync(creds, ct);
            var discounts = await _shopify.GetDiscountsAsync(creds, ct);
            var pages = await _shopify.GetPagesAsync(creds, ct);

            // Tam yenileme: eski kayıtlar silinir (alt kayıtlar cascade), sonra yeniden eklenir.
            // İki SaveChanges: benzersiz (tenant, ShopifyId) indeksi silme+ekleme aynı anda çakışmasın.
            _db.SyncedProducts.RemoveRange(await _db.SyncedProducts.Include(p => p.Variants).ToListAsync(ct));
            _db.SyncedCollections.RemoveRange(await _db.SyncedCollections.Include(c => c.Products).ToListAsync(ct));
            _db.SyncedOrders.RemoveRange(await _db.SyncedOrders.Include(o => o.Lines).ToListAsync(ct));
            _db.SyncedCustomers.RemoveRange(await _db.SyncedCustomers.ToListAsync(ct));
            _db.SyncedDiscounts.RemoveRange(await _db.SyncedDiscounts.ToListAsync(ct));
            _db.SyncedPages.RemoveRange(await _db.SyncedPages.ToListAsync(ct));
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

            foreach (var cu in customers)
            {
                _db.SyncedCustomers.Add(new SyncedCustomer
                {
                    ShopifyCustomerId = cu.CustomerId,
                    Email = cu.Email,
                    FirstName = cu.FirstName,
                    LastName = cu.LastName,
                    Phone = cu.Phone,
                    OrdersCount = cu.OrdersCount,
                    TotalSpent = cu.TotalSpent,
                    ShopifyCreatedAt = cu.CreatedAt,
                    ShopifyUpdatedAt = cu.UpdatedAt
                });
            }

            foreach (var o in orders)
            {
                _db.SyncedOrders.Add(new SyncedOrder
                {
                    ShopifyOrderId = o.OrderId,
                    Name = o.Name,
                    ShopifyCustomerId = o.CustomerId,
                    Email = o.Email,
                    FinancialStatus = o.FinancialStatus,
                    FulfillmentStatus = o.FulfillmentStatus,
                    TotalPrice = o.TotalPrice,
                    Currency = o.Currency,
                    ShopifyCreatedAt = o.CreatedAt,
                    ShopifyUpdatedAt = o.UpdatedAt,
                    Lines = o.Lines.Select(l => new SyncedOrderLine
                    {
                        ShopifyLineId = l.LineId,
                        ShopifyProductId = l.ProductId,
                        ShopifyVariantId = l.VariantId,
                        Sku = l.Sku,
                        Title = l.Title,
                        Quantity = l.Quantity,
                        Price = l.Price
                    }).ToList()
                });
            }

            foreach (var d in discounts)
            {
                _db.SyncedDiscounts.Add(new SyncedDiscount
                {
                    ShopifyDiscountId = d.DiscountId,
                    Title = d.Title,
                    Code = d.Code,
                    DiscountType = d.DiscountType,
                    Value = d.Value,
                    Currency = d.Currency,
                    StartsAt = d.StartsAt,
                    EndsAt = d.EndsAt,
                    Status = d.Status,
                    UsageCount = d.UsageCount
                });
            }

            foreach (var pg in pages)
            {
                _db.SyncedPages.Add(new SyncedPage
                {
                    ShopifyPageId = pg.PageId,
                    Title = pg.Title,
                    Handle = pg.Handle,
                    BodyHtml = pg.BodyHtml,
                    Status = pg.Status,
                    ShopifyUpdatedAt = pg.UpdatedAt
                });
            }

            var state = await GetOrCreateStateAsync(merchantId, ct);
            sw.Stop();
            state.LastSyncAt = DateTimeOffset.UtcNow;
            state.LastStatus = "success";
            state.LastError = null;
            state.LastTrigger = trigger;
            state.DurationMs = sw.ElapsedMilliseconds;
            state.ProductCount = products.Count;
            state.CollectionCount = collections.Count;
            state.OrderCount = orders.Count;
            state.CustomerCount = customers.Count;
            state.DiscountCount = discounts.Count;
            state.PageCount = pages.Count;

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Mağaza senkronu ({Trigger}): store={Store} ürün={P} koleksiyon={C} sipariş={O} müşteri={Cu} indirim={D} sayfa={Pg} süre={Ms}ms",
                trigger, integration.ShopDomain, products.Count, collections.Count, orders.Count,
                customers.Count, discounts.Count, pages.Count, sw.ElapsedMilliseconds);

            return new StoreSyncResult(products.Count, collections.Count, orders.Count,
                customers.Count, discounts.Count, pages.Count, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            await TryRecordFailureAsync(merchantId, trigger, ex.Message, sw.ElapsedMilliseconds, ct);
            throw;
        }
    }

    private async Task<StoreSyncState> GetOrCreateStateAsync(Guid merchantId, CancellationToken ct)
    {
        var state = await _db.SyncStates.FirstOrDefaultAsync(s => s.TenantId == merchantId, ct);
        if (state is null)
        {
            state = new StoreSyncState { TenantId = merchantId };
            _db.SyncStates.Add(state);
        }
        return state;
    }

    /// <summary>Hata durumunu kaydetmeye çalışır; bu da başarısız olursa senkron hatasını gölgelemez.</summary>
    private async Task TryRecordFailureAsync(Guid merchantId, string trigger, string error, long ms, CancellationToken ct)
    {
        try
        {
            _db.ChangeTracker.Clear();
            var state = await GetOrCreateStateAsync(merchantId, ct);
            state.LastSyncAt = DateTimeOffset.UtcNow;
            state.LastStatus = "failed";
            state.LastError = error.Length > 2000 ? error[..2000] : error;
            state.LastTrigger = trigger;
            state.DurationMs = ms;
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception inner)
        {
            _logger.LogWarning(inner, "Senkron hata durumu kaydedilemedi: merchant={MerchantId}", merchantId);
        }
    }
}
