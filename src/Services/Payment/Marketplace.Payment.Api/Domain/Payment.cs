using Marketplace.BuildingBlocks.Domain;

namespace Marketplace.Payment.Api.Domain;

public enum PaymentStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2
}

public class Payment : AuditableTenantEntity
{
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "TRY";
    public string Provider { get; set; } = default!;
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string? ProviderPaymentId { get; set; }
    public string? FailureReason { get; set; }
}
