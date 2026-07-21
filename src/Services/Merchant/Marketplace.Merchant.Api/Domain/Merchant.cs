using Marketplace.BuildingBlocks.Domain;

namespace Marketplace.Merchant.Api.Domain;

public enum MerchantStatus
{
    Pending = 0,
    Active = 1,
    Suspended = 2
}

/// <summary>
/// Pazaryerindeki bir satıcı. Merchant.Id aynı zamanda tenant kimliğidir
/// (JWT'deki tenant_id claim'i bu Id'ye eşlenir).
/// </summary>
public class Merchant : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public MerchantStatus Status { get; set; } = MerchantStatus.Pending;

    /// <summary>Pazaryeri komisyon oranı (0-1 arası, ör. 0.10 = %10).</summary>
    public decimal CommissionRate { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
