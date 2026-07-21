using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.Contracts;
using Marketplace.Inventory.Api.Domain;
using Marketplace.Inventory.Api.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Inventory.Api.Consumers;

/// <summary>Catalog'da ürün oluşunca 0 stoklu envanter kaydı açar (idempotent).</summary>
public sealed class ProductCreatedConsumer : IConsumer<ProductCreatedIntegrationEvent>
{
    private readonly InventoryDbContext _db;
    private readonly ITenantContext _tenant;

    public ProductCreatedConsumer(InventoryDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task Consume(ConsumeContext<ProductCreatedIntegrationEvent> context)
    {
        var e = context.Message;
        if (e.TenantId is null) return;

        // Consumer HTTP dışında çalışır: tenant'ı event'ten kur.
        _tenant.SetTenant(e.TenantId, isPlatformScope: false);

        var exists = await _db.Items.AnyAsync(i => i.ProductId == e.ProductId, context.CancellationToken);
        if (exists) return;

        _db.Items.Add(new InventoryItem { ProductId = e.ProductId, Sku = e.Sku, QuantityOnHand = 0 });
        await _db.SaveChangesAsync(context.CancellationToken);
    }
}
