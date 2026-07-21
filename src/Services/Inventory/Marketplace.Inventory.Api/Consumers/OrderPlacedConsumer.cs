using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Outbox;
using Marketplace.Contracts;
using Marketplace.Inventory.Api.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Inventory.Api.Consumers;

/// <summary>
/// Sipariş verilince stok rezervasyonu dener. Sonucu (StockReserved / StockReservationFailed)
/// outbox üzerinden Order servisine bildirir.
/// Not: Faz 1'de idempotency basit; prod'da işlenmiş OrderId'ler inbox ile izlenecek.
/// </summary>
public sealed class OrderPlacedConsumer : IConsumer<OrderPlacedIntegrationEvent>
{
    private readonly InventoryDbContext _db;
    private readonly ITenantContext _tenant;

    public OrderPlacedConsumer(InventoryDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task Consume(ConsumeContext<OrderPlacedIntegrationEvent> context)
    {
        var e = context.Message;
        if (e.TenantId is null) return;
        _tenant.SetTenant(e.TenantId, isPlatformScope: false);

        var productIds = e.Lines.Select(l => l.ProductId).ToList();
        var items = await _db.Items
            .Where(i => productIds.Contains(i.ProductId))
            .ToListAsync(context.CancellationToken);
        var byProduct = items.ToDictionary(i => i.ProductId);

        var insufficient = new List<string>();
        foreach (var line in e.Lines)
        {
            if (!byProduct.TryGetValue(line.ProductId, out var item) || item.Available < line.Quantity)
                insufficient.Add(line.Sku);
        }

        if (insufficient.Count > 0)
        {
            _db.EnqueueIntegrationEvent(new StockReservationFailedIntegrationEvent
            {
                TenantId = e.TenantId,
                OrderId = e.OrderId,
                Reason = $"Yetersiz stok: {string.Join(", ", insufficient)}"
            });
        }
        else
        {
            foreach (var line in e.Lines)
                byProduct[line.ProductId].QuantityReserved += line.Quantity;

            _db.EnqueueIntegrationEvent(new StockReservedIntegrationEvent
            {
                TenantId = e.TenantId,
                OrderId = e.OrderId
            });
        }

        await _db.SaveChangesAsync(context.CancellationToken);
    }
}
