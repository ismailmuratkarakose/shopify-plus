using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.Contracts;
using Marketplace.Inventory.Api.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Inventory.Api.Consumers;

/// <summary>Telafi: ödeme başarısız olunca sipariş için rezerve edilen stoğu geri bırakır.</summary>
public sealed class StockReleaseRequestedConsumer : IConsumer<StockReleaseRequestedIntegrationEvent>
{
    private readonly InventoryDbContext _db;
    private readonly ITenantContext _tenant;

    public StockReleaseRequestedConsumer(InventoryDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task Consume(ConsumeContext<StockReleaseRequestedIntegrationEvent> context)
    {
        var e = context.Message;
        if (e.TenantId is null) return;
        _tenant.SetTenant(e.TenantId, isPlatformScope: false);

        var productIds = e.Lines.Select(l => l.ProductId).ToList();
        var items = await _db.Items.Where(i => productIds.Contains(i.ProductId)).ToListAsync(context.CancellationToken);
        var byProduct = items.ToDictionary(i => i.ProductId);

        foreach (var line in e.Lines)
        {
            if (byProduct.TryGetValue(line.ProductId, out var item))
                item.QuantityReserved = Math.Max(0, item.QuantityReserved - line.Quantity);
        }

        await _db.SaveChangesAsync(context.CancellationToken);
    }
}
