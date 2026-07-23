using Marketplace.BuildingBlocks.Domain;

namespace Marketplace.ShopifySync.Api.Domain;

/// <summary>Bir merchant'ın Shopify entegrasyonu (read-model). Token at-rest şifreli.</summary>
public class ShopifyIntegration : AuditableStoreEntity
{
    public string ShopDomain { get; set; } = default!;
    public string EncryptedAccessToken { get; set; } = default!;
    public bool IsActive { get; set; } = true;
}

/// <summary>Pazaryeri ürünü ↔ Shopify ürünü eşlemesi (Sku anahtarlı; iki yönlü senkron + döngü önleme).</summary>
public class ProductMapping : AuditableStoreEntity
{
    public string Sku { get; set; } = default!;
    public long ShopifyProductId { get; set; }
    /// <summary>Pazaryeri ürün kimliği. Outbound'da bilinir; inbound'da bilinmiyorsa Guid.Empty.</summary>
    public Guid MarketplaceProductId { get; set; }
}

/// <summary>Webhook idempotency kaydı (X-Shopify-Webhook-Id ile tekrarları eler). Tenant'tan bağımsız.</summary>
public class WebhookInbox
{
    public string WebhookId { get; set; } = default!;
    public string Topic { get; set; } = default!;
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
}
