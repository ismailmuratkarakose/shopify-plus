using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Security;
using Marketplace.Contracts;
using Marketplace.ShopifySync.Api.Domain;
using Marketplace.ShopifySync.Api.Infrastructure;
using Marketplace.ShopifySync.Api.Shopify;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.ShopifySync.Api.Consumers;

/// <summary>
/// Outbound: Catalog'da ürün oluşunca, merchant'ın Shopify entegrasyonu varsa ürünü Shopify'a push eder
/// ve pazaryeri↔Shopify eşlemesini saklar.
/// Döngü önleme: Shopify kaynaklı ürün (Source=shopify) tekrar Shopify'a push edilmez.
/// Idempotency: eşleme zaten varsa update (mevcut ShopifyProductId ile), yoksa create.
/// </summary>
public sealed class ProductCreatedConsumer : IConsumer<ProductCreatedIntegrationEvent>
{
    private readonly ShopifySyncDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IShopifyClient _shopify;
    private readonly ISecretProtector _protector;
    private readonly ILogger<ProductCreatedConsumer> _logger;

    public ProductCreatedConsumer(
        ShopifySyncDbContext db,
        ITenantContext tenant,
        IShopifyClient shopify,
        ISecretProtector protector,
        ILogger<ProductCreatedConsumer> logger)
    {
        _db = db;
        _tenant = tenant;
        _shopify = shopify;
        _protector = protector;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ProductCreatedIntegrationEvent> context)
    {
        var e = context.Message;
        if (e.TenantId is null) return;

        // Döngü önleme: değişiklik Shopify'dan geldiyse geri push etme.
        if (string.Equals(e.Source, "shopify", StringComparison.OrdinalIgnoreCase))
            return;

        _tenant.SetTenant(e.TenantId, isPlatformScope: false);

        var integration = await _db.Integrations
            .FirstOrDefaultAsync(i => i.TenantId == e.TenantId && i.IsActive, context.CancellationToken);
        if (integration is null)
            return; // Merchant'ın aktif Shopify entegrasyonu yok → push yok.

        var store = new ShopifyStoreCredentials(
            integration.ShopDomain,
            _protector.Unprotect(integration.EncryptedAccessToken));

        var mapping = await _db.ProductMappings
            .FirstOrDefaultAsync(m => m.Sku == e.Sku, context.CancellationToken);

        var pushed = await _shopify.UpsertProductAsync(
            store,
            new ShopifyProductPush(e.Sku, e.Title, e.Description, e.Price, e.Currency),
            mapping?.ShopifyProductId,
            context.CancellationToken);

        if (mapping is null)
        {
            _db.ProductMappings.Add(new ProductMapping
            {
                TenantId = e.TenantId.Value,
                MarketplaceProductId = e.ProductId,
                ShopifyProductId = pushed.ShopifyProductId,
                Sku = e.Sku
            });
        }
        else
        {
            mapping.ShopifyProductId = pushed.ShopifyProductId;
        }

        await _db.SaveChangesAsync(context.CancellationToken);
        _logger.LogInformation("Ürün Shopify'a senkronlandı: {Sku} -> {ShopifyId}", e.Sku, pushed.ShopifyProductId);
    }
}
