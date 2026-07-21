namespace Marketplace.BuildingBlocks.Domain;

/// <summary>Merchant'a ait (tenant izolasyonuna tabi) her kayıt bunu uygular.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}

/// <summary>Oluşturma/güncelleme denetim alanları.</summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }
    DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Merchant'a ait denetlenebilir entity'ler için ortak taban.</summary>
public abstract class AuditableTenantEntity : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
