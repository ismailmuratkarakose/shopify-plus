using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Outbox;
using Marketplace.Contracts;
using Marketplace.Payment.Api.Domain;
using Marketplace.Payment.Api.Infrastructure;
using Marketplace.Payment.Api.Providers;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Payment.Api.Consumers;

/// <summary>
/// Ödeme talebini işler: sağlayıcıyı çöz → tahsil et → Payment kaydı + sonuç event'i (outbox).
/// Idempotency: aynı sipariş için başarılı ödeme varsa PaymentSucceeded yeniden yayınlanır.
/// NOT: dış tahsilat ile DB commit tek adımda; prod'da ödeme idempotency-key + iki-aşamalı kayıt önerilir.
/// </summary>
public sealed class PaymentRequestedConsumer : IConsumer<PaymentRequestedIntegrationEvent>
{
    private readonly PaymentDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IPaymentProviderResolver _resolver;

    public PaymentRequestedConsumer(PaymentDbContext db, ITenantContext tenant, IPaymentProviderResolver resolver)
    {
        _db = db;
        _tenant = tenant;
        _resolver = resolver;
    }

    public async Task Consume(ConsumeContext<PaymentRequestedIntegrationEvent> context)
    {
        var e = context.Message;
        if (e.TenantId is null) return;
        _tenant.SetTenant(e.TenantId, isPlatformScope: false);

        var existing = await _db.Payments
            .FirstOrDefaultAsync(p => p.OrderId == e.OrderId && p.Status == PaymentStatus.Succeeded, context.CancellationToken);
        if (existing is not null)
        {
            // Zaten ödenmiş: sonucu yeniden bildir (idempotent).
            _db.EnqueueIntegrationEvent(new PaymentSucceededIntegrationEvent
            {
                TenantId = e.TenantId, OrderId = e.OrderId, PaymentId = existing.Id,
                Provider = existing.Provider, ProviderPaymentId = existing.ProviderPaymentId ?? ""
            });
            await _db.SaveChangesAsync(context.CancellationToken);
            return;
        }

        var (provider, credentials) = await _resolver.ResolveAsync(e.TenantId.Value, context.CancellationToken);
        var result = await provider.ChargeAsync(credentials, new PaymentCharge(e.OrderId, e.Amount, e.Currency), context.CancellationToken);

        var payment = new Domain.Payment
        {
            OrderId = e.OrderId,
            Amount = e.Amount,
            Currency = e.Currency,
            Provider = provider.Name,
            Status = result.Success ? PaymentStatus.Succeeded : PaymentStatus.Failed,
            ProviderPaymentId = result.ProviderPaymentId,
            FailureReason = result.Error
        };
        _db.Payments.Add(payment);

        if (result.Success)
            _db.EnqueueIntegrationEvent(new PaymentSucceededIntegrationEvent
            {
                TenantId = e.TenantId, OrderId = e.OrderId, PaymentId = payment.Id,
                Provider = provider.Name, ProviderPaymentId = result.ProviderPaymentId ?? ""
            });
        else
            _db.EnqueueIntegrationEvent(new PaymentFailedIntegrationEvent
            {
                TenantId = e.TenantId, OrderId = e.OrderId, Reason = result.Error ?? "Ödeme reddedildi."
            });

        await _db.SaveChangesAsync(context.CancellationToken);
    }
}
