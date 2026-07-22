using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Outbox;
using Marketplace.Contracts;
using Marketplace.Order.Api.Domain;
using Marketplace.Order.Api.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Order.Api.Consumers;

/// <summary>Ödeme başarılı → sipariş Paid.</summary>
public sealed class PaymentSucceededConsumer : IConsumer<PaymentSucceededIntegrationEvent>
{
    private readonly OrderDbContext _db;
    private readonly ITenantContext _tenant;

    public PaymentSucceededConsumer(OrderDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task Consume(ConsumeContext<PaymentSucceededIntegrationEvent> context)
    {
        var e = context.Message;
        if (e.TenantId is null) return;
        _tenant.SetTenant(e.TenantId, isPlatformScope: false);

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == e.OrderId, context.CancellationToken);
        if (order is null || order.Status != OrderStatus.AwaitingPayment) return; // idempotent

        order.Status = OrderStatus.Paid;
        await _db.SaveChangesAsync(context.CancellationToken);
    }
}

/// <summary>Ödeme başarısız → sipariş PaymentFailed + rezerve stoğun geri bırakılması (telafi).</summary>
public sealed class PaymentFailedConsumer : IConsumer<PaymentFailedIntegrationEvent>
{
    private readonly OrderDbContext _db;
    private readonly ITenantContext _tenant;

    public PaymentFailedConsumer(OrderDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task Consume(ConsumeContext<PaymentFailedIntegrationEvent> context)
    {
        var e = context.Message;
        if (e.TenantId is null) return;
        _tenant.SetTenant(e.TenantId, isPlatformScope: false);

        var order = await _db.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == e.OrderId, context.CancellationToken);
        if (order is null || order.Status != OrderStatus.AwaitingPayment) return;

        order.Status = OrderStatus.PaymentFailed;
        order.StatusReason = e.Reason;

        _db.EnqueueIntegrationEvent(new StockReleaseRequestedIntegrationEvent
        {
            TenantId = e.TenantId,
            OrderId = order.Id,
            Lines = order.Items.Select(i => new OrderLine
            {
                ProductId = i.ProductId,
                Sku = i.Sku,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList()
        });

        await _db.SaveChangesAsync(context.CancellationToken);
    }
}
