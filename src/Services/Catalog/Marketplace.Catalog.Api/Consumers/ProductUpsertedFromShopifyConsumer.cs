using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Outbox;
using Marketplace.Contracts;
using Marketplace.Catalog.Api.Domain;
using Marketplace.Catalog.Api.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Catalog.Api.Consumers;

/// <summary>
/// Inbound: Shopify'dan gelen ürünü Catalog'a upsert eder (Source=shopify).
/// Çakışma çözümü: ürün pazaryeri kaynaklı ve yerel değişiklik Shopify'ınkinden yeniyse atlanır (marketplace kazanır).
/// Yeni ürün oluşturulursa ProductCreated(Source=shopify) yayınlanır → Inventory stok kaydı açar,
/// ShopifySync outbound Source=shopify olduğu için geri push etmez (döngü önleme).
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

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Sku == e.Sku, context.CancellationToken);

        if (product is null)
        {
            product = new Product
            {
                Sku = e.Sku,
                Title = e.Title,
                Description = e.Description,
                Price = e.Price,
                Currency = e.Currency,
                Source = ProductSource.Shopify,
                ShopifyProductId = e.ShopifyProductId,
                LastSyncedAt = e.ShopifyUpdatedAt
            };
            _db.Products.Add(product);

            _db.EnqueueIntegrationEvent(new ProductCreatedIntegrationEvent
            {
                TenantId = e.TenantId,
                ProductId = product.Id,
                Sku = product.Sku,
                Title = product.Title,
                Description = product.Description,
                Price = product.Price,
                Currency = product.Currency,
                Source = ProductSource.Shopify
            });

            await _db.SaveChangesAsync(context.CancellationToken);
            _logger.LogInformation("Shopify'dan ürün eklendi: {Sku}", e.Sku);
            return;
        }

        // Çakışma: pazaryeri kaynaklı ve yerel güncelleme daha yeniyse Shopify'ı yok say.
        if (product.Source == ProductSource.Marketplace &&
            product.UpdatedAt is { } localUpdated && localUpdated > e.ShopifyUpdatedAt)
        {
            _logger.LogInformation("Çakışma: {Sku} pazaryeri sürümü daha yeni, Shopify güncellemesi atlandı.", e.Sku);
            return;
        }

        product.Title = e.Title;
        product.Description = e.Description;
        product.Price = e.Price;
        product.Currency = e.Currency;
        product.Source = ProductSource.Shopify;
        product.ShopifyProductId = e.ShopifyProductId;
        product.LastSyncedAt = e.ShopifyUpdatedAt;

        await _db.SaveChangesAsync(context.CancellationToken);
        _logger.LogInformation("Shopify'dan ürün güncellendi: {Sku}", e.Sku);
    }
}
