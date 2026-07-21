using Marketplace.BuildingBlocks.Domain;

namespace Marketplace.ShopifySync.Api.Domain;

/// <summary>Bir merchant'ın Shopify entegrasyonu (read-model). Token at-rest şifreli.</summary>
public class ShopifyIntegration : AuditableTenantEntity
{
    public string ShopDomain { get; set; } = default!;
    public string EncryptedAccessToken { get; set; } = default!;
    public bool IsActive { get; set; } = true;
}

/// <summary>Pazaryeri ürünü ↔ Shopify ürünü eşlemesi (outbound senkron + döngü önleme).</summary>
public class ProductMapping : AuditableTenantEntity
{
    public Guid MarketplaceProductId { get; set; }
    public long ShopifyProductId { get; set; }
    public string Sku { get; set; } = default!;
}
