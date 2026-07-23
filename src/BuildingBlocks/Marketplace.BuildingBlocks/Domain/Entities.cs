namespace Marketplace.BuildingBlocks.Domain;

/// <summary>Bir mağazaya ait (mağaza izolasyonuna tabi) her kayıt bunu uygular.</summary>
public interface IStoreOwned
{
    Guid StoreId { get; set; }
}

/// <summary>Oluşturma/güncelleme denetim alanları.</summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }
    DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Pazaryeri (platform) seviyesindeki denetlenebilir entity'ler için taban — mağaza boyutu YOKTUR.
/// CMS içeriği, ürün master'ı gibi pazaryerinin bütününe ait veriler bunu kullanır.
/// </summary>
public abstract class AuditableEntity : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Bir mağazaya ait denetlenebilir entity'ler için ortak taban.</summary>
public abstract class AuditableStoreEntity : AuditableEntity, IStoreOwned
{
    public Guid StoreId { get; set; }
}
