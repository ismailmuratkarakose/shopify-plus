using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Outbox;
using Marketplace.Contracts;
using Marketplace.Catalog.Api.Domain;
using Marketplace.Catalog.Api.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Catalog.Api.Consumers;

/// <summary>
/// Inbound: Shopify'dan gelen ürünü barkodla global master'a eşler ve o merchant'ın teklifini (Offer) upsert eder.
/// - Barkod master'ı yoksa oluşturulur (ilk gören açar).
/// - Merchant'ın o master için offer'ı yoksa oluşturulur (Source=shopify) → ProductCreated yayınlanır
///   (Inventory stok açar; outbound Source=shopify olduğundan Shopify'a geri push edilmez — döngü önleme).
/// - Offer varsa çakışma çözümü: pazaryeri kaynaklı ve yerel değişiklik daha yeniyse Shopify atlanır.
/// </summary>
public sealed class ProductUpsertedFromShopifyConsumer : IConsumer<ProductUpsertedFromShopifyIntegrationEvent>
{
    private readonly CatalogDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ILogger<ProductUpsertedFromShopifyConsumer> _logger;

    public ProductUpsertedFromShopifyConsumer(CatalogDbContext db, ITenantContext tenant, ILogger<ProductUpsertedFromShopifyConsumer> logger)
    {
        _db = db;
        _tenant = tenant;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ProductUpsertedFromShopifyIntegrationEvent> context)
    {
        var e = context.Message;
        if (e.TenantId is null) return;
        _tenant.SetTenant(e.TenantId, isPlatformScope: false);

        var barcode = string.IsNullOrWhiteSpace(e.Barcode) ? e.Sku : e.Barcode!;

        // 1) Master'ı barkodla bul/oluştur (global).
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Barcode == barcode, context.CancellationToken);
        if (product is null)
        {
            product = new Product { Barcode = barcode, Title = e.Title, Description = e.Description };
            _db.Products.Add(product);
        }

        // 2) Bu merchant'ın offer'ını bul (tenant-filtered).
        var offer = await _db.Offers.FirstOrDefaultAsync(o => o.ProductId == product.Id, context.CancellationToken);

        if (offer is null)
        {
            offer = new Offer
            {
                Product = product,
                Sku = e.Sku,
                Price = e.Price,
                Currency = e.Currency,
                Source = ProductSource.Shopify,
                ShopifyProductId = e.ShopifyProductId,
                LastSyncedAt = e.ShopifyUpdatedAt
            };
            _db.Offers.Add(offer);

            _db.EnqueueIntegrationEvent(new ProductCreatedIntegrationEvent
            {
                TenantId = e.TenantId,
                ProductId = product.Id,
                OfferId = offer.Id,
                Barcode = barcode,
                Sku = e.Sku,
                Title = product.Title,
                Description = product.Description,
                Price = offer.Price,
                Currency = offer.Currency,
                Source = ProductSource.Shopify
            });

            await _db.SaveChangesAsync(context.CancellationToken);
            _logger.LogInformation("Shopify'dan teklif eklendi: barkod {Barcode} (merchant {Tenant})", barcode, e.TenantId);
            return;
        }

        // 3) Çakışma: teklif pazaryeri kaynaklı ve yerel güncelleme daha yeniyse Shopify'ı yok say.
        if (offer.Source == ProductSource.Marketplace &&
            offer.UpdatedAt is { } localUpdated && localUpdated > e.ShopifyUpdatedAt)
        {
            _logger.LogInformation("Çakışma: barkod {Barcode} pazaryeri sürümü daha yeni, Shopify atlandı.", barcode);
            return;
        }

        offer.Price = e.Price;
        offer.Currency = e.Currency;
        offer.Source = ProductSource.Shopify;
        offer.ShopifyProductId = e.ShopifyProductId;
        offer.LastSyncedAt = e.ShopifyUpdatedAt;
        await _db.SaveChangesAsync(context.CancellationToken);
        _logger.LogInformation("Shopify'dan teklif güncellendi: barkod {Barcode}", barcode);
    }
}
