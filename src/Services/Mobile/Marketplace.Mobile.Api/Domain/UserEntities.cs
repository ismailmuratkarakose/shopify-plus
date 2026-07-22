using Marketplace.BuildingBlocks.Domain;

namespace Marketplace.Mobile.Api.Domain;

/// <summary>Müşterinin favori ürünü. Kullanıcı JWT'deki kimlikle (sub) tanımlanır.</summary>
public class FavoriteProduct : AuditableTenantEntity
{
    public string UserRef { get; set; } = default!;
    public long ShopifyProductId { get; set; }
}

/// <summary>Son görüntülenen ürün kaydı (kullanıcı başına sınırlı sayıda tutulur).</summary>
public class RecentlyViewedProduct : AuditableTenantEntity
{
    public string UserRef { get; set; } = default!;
    public long ShopifyProductId { get; set; }
    public DateTimeOffset ViewedAt { get; set; } = DateTimeOffset.UtcNow;
}
