namespace Marketplace.Reporting.Api.Domain;

public enum SaleStatus
{
    Pending = 0,
    Paid = 1,
    Failed = 2
}

/// <summary>
/// Merchant komisyon oranının read-model kopyası (MerchantRegistered event'inden beslenir).
/// TenantId = MerchantId. Komisyon hesabı için satış olayında burası okunur.
/// </summary>
public class MerchantRate
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = default!;
    public decimal CommissionRate { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Sipariş başına tek satış kaydı (read-model). OrderPlaced ile Pending oluşur,
/// PaymentSucceeded ile Paid + komisyon hesaplanır, PaymentFailed ile Failed olur.
/// OrderId birincil anahtar → consumer'lar idempotent upsert yapabilir.
/// </summary>
public class SalesFact
{
    public Guid OrderId { get; set; }
    public Guid TenantId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = default!;
    public SaleStatus Status { get; set; } = SaleStatus.Pending;

    /// <summary>Ödeme anındaki komisyon oranı anlık kopyası.</summary>
    public decimal CommissionRate { get; set; }
    public decimal CommissionAmount { get; set; }
    public decimal NetAmount { get; set; }

    public DateTimeOffset PlacedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
}
