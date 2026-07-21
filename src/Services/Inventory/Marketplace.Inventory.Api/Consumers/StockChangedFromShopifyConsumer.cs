using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.Contracts;
using Marketplace.Inventory.Api.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Inventory.Api.Consumers;

/// <summary>Inbound: Shopify'dan gelen stok değişimini ilgili SKU'nun envanterine uygular.</summary>
public sealed class StockChangedFromShopifyConsumer : IConsumer<StockChangedFromShopifyIntegrationEvent>
{
    private readonly InventoryDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ILogger<StockChangedFromShopifyConsumer> _logger;

    public StockChangedFromShopifyConsumer(InventoryDbContext db, ITenantContext tenant, ILogger<StockChangedFromShopifyConsumer> logger)
    {
        _db = db;
        _tenant = tenant;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<StockChangedFromShopifyIntegrationEvent> context)
    {
        var e = context.Message;
        if (e.TenantId is null) return;
        _tenant.SetTenant(e.TenantId, isPlatformScope: false);

        var item = await _db.Items.FirstOrDefaultAsync(i => i.Sku == e.Sku, context.CancellationToken);
        if (item is null)
        {
            // Ürün/envanter kaydı (ProductCreated ile) henüz gelmemiş olabilir → retry için exception at.
            _logger.LogWarning("Shopify stok webhook'u: {Sku} için envanter kaydı yok, retry.", e.Sku);
            throw new InvalidOperationException($"Envanter kaydı yok: {e.Sku} (retry bekleniyor).");
        }

        item.QuantityOnHand = e.QuantityOnHand;
        await _db.SaveChangesAsync(context.CancellationToken);
        _logger.LogInformation("Shopify'dan stok güncellendi: {Sku} -> {Qty}", e.Sku, e.QuantityOnHand);
    }
}
