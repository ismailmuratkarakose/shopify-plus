using Marketplace.Contracts;
using Marketplace.Reporting.Api.Domain;
using Marketplace.Reporting.Api.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Reporting.Api.Consumers;

/// <summary>Merchant kaydı/oranı read-model'e alınır. Komisyon hesabı bunu okur.</summary>
public sealed class MerchantRegisteredConsumer(ReportingDbContext db) : IConsumer<MerchantRegisteredIntegrationEvent>
{
    public async Task Consume(ConsumeContext<MerchantRegisteredIntegrationEvent> context)
    {
        var e = context.Message;
        var rate = await db.MerchantRates.FirstOrDefaultAsync(r => r.TenantId == e.MerchantId, context.CancellationToken);
        if (rate is null)
        {
            db.MerchantRates.Add(new MerchantRate { TenantId = e.MerchantId, Name = e.Name, CommissionRate = e.CommissionRate });
        }
        else
        {
            rate.Name = e.Name;
            rate.CommissionRate = e.CommissionRate;
            rate.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(context.CancellationToken);
    }
}

/// <summary>Sipariş verildiğinde Pending satış kaydı açılır (tutar burada bilinir).</summary>
public sealed class OrderPlacedConsumer(ReportingDbContext db) : IConsumer<OrderPlacedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<OrderPlacedIntegrationEvent> context)
    {
        var e = context.Message;
        if (e.TenantId is null) return;

        var exists = await db.Sales.AnyAsync(s => s.OrderId == e.OrderId, context.CancellationToken);
        if (exists) return; // idempotent

        db.Sales.Add(new SalesFact
        {
            OrderId = e.OrderId,
            TenantId = e.TenantId.Value,
            Amount = e.Total,
            Currency = e.Currency,
            Status = SaleStatus.Pending,
            PlacedAt = e.OccurredOn
        });
        await db.SaveChangesAsync(context.CancellationToken);
    }
}

/// <summary>Ödeme başarılı → satış Paid, komisyon = tutar × merchant oranı (anlık kopya).</summary>
public sealed class PaymentSucceededConsumer(ReportingDbContext db) : IConsumer<PaymentSucceededIntegrationEvent>
{
    public async Task Consume(ConsumeContext<PaymentSucceededIntegrationEvent> context)
    {
        var e = context.Message;
        var fact = await db.Sales.FirstOrDefaultAsync(s => s.OrderId == e.OrderId, context.CancellationToken);
        // OrderPlaced henüz işlenmemiş olabilir → retry (Program'da UseMessageRetry).
        if (fact is null)
            throw new InvalidOperationException($"Satış kaydı yok, OrderPlaced bekleniyor: {e.OrderId}");
        if (fact.Status == SaleStatus.Paid) return; // idempotent

        var rate = (await db.MerchantRates.FirstOrDefaultAsync(r => r.TenantId == fact.TenantId, context.CancellationToken))
            ?.CommissionRate ?? 0m;

        fact.Status = SaleStatus.Paid;
        fact.CommissionRate = rate;
        fact.CommissionAmount = Math.Round(fact.Amount * rate, 2, MidpointRounding.AwayFromZero);
        fact.NetAmount = fact.Amount - fact.CommissionAmount;
        fact.PaidAt = e.OccurredOn;
        await db.SaveChangesAsync(context.CancellationToken);
    }
}

/// <summary>Ödeme başarısız → satış Failed (ciroya girmez).</summary>
public sealed class PaymentFailedConsumer(ReportingDbContext db) : IConsumer<PaymentFailedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<PaymentFailedIntegrationEvent> context)
    {
        var e = context.Message;
        var fact = await db.Sales.FirstOrDefaultAsync(s => s.OrderId == e.OrderId, context.CancellationToken);
        if (fact is null)
            throw new InvalidOperationException($"Satış kaydı yok, OrderPlaced bekleniyor: {e.OrderId}");
        if (fact.Status == SaleStatus.Failed) return; // idempotent

        fact.Status = SaleStatus.Failed;
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
