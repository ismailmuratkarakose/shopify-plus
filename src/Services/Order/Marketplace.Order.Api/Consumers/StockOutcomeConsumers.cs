using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Outbox;
using Marketplace.Contracts;
using Marketplace.Order.Api.Domain;
using Marketplace.Order.Api.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Order.Api.Consumers;

/// <summary>Stok rezerve edildi → sipariş ödeme bekliyor; ödeme talebi yayınlanır.</summary>
public sealed class StockReservedConsumer : IConsumer<StockReservedIntegrationEvent>
{
    private readonly OrderDbContext _db;
    private readonly ITenantContext _tenant;

    public StockReservedConsumer(OrderDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task Consume(ConsumeContext<StockReservedIntegrationEvent> context)
    {
        var e = context.Message;
        if (e.TenantId is null) return;
        _tenant.SetTenant(e.TenantId, isPlatformScope: false);

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == e.OrderId, context.CancellationToken);
        if (order is null || order.Status != OrderStatus.Pending) return; // idempotent

        order.Status = OrderStatus.AwaitingPayment;
        _db.EnqueueIntegrationEvent(new PaymentRequestedIntegrationEvent
        {
            TenantId = e.TenantId,
            OrderId = order.Id,
            Amount = order.TotalAmount,
            Currency = order.Currency
        });
        await _db.SaveChangesAsync(context.CancellationToken);
    }
}

/// <summary>Yeterli stok yoksa siparişi reddeder.</summary>
public sealed class StockReservationFailedConsumer : IConsumer<StockReservationFailedIntegrationEvent>
{
    private readonly OrderDbContext _db;
    private readonly ITenantContext _tenant;

    public StockReservationFailedConsumer(OrderDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task Consume(ConsumeContext<StockReservationFailedIntegrationEvent> context)
    {
        var e = context.Message;
        if (e.TenantId is null) return;
        _tenant.SetTenant(e.TenantId, isPlatformScope: false);

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == e.OrderId, context.CancellationToken);
        if (order is null || order.Status != OrderStatus.Pending) return;

        order.Status = OrderStatus.Rejected;
        order.StatusReason = e.Reason;
        await _db.SaveChangesAsync(context.CancellationToken);
    }
}
