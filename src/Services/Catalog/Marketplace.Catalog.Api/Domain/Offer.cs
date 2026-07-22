using Marketplace.BuildingBlocks.Domain;

namespace Marketplace.Catalog.Api.Domain;

/// <summary>
/// Bir merchant'ın (tenant) belirli bir <see cref="Product"/> master'ını satma teklifi:
/// kendi fiyatı ve (isteğe bağlı) kendi SKU'su. Stok Inventory'de (tenant, ProductId) tutulur.
/// Bir merchant aynı master için tek offer verir (TenantId+ProductId benzersiz).
/// </summary>
public class Offer : AuditableTenantEntity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = default!;

    /// <summary>Merchant'ın kendi ürün kodu (opsiyonel). Stok/Shopify eşlemesinde kullanılır.</summary>
    public string? Sku { get; set; }

    public decimal Price { get; set; }
    public string Currency { get; set; } = "TRY";
    public bool IsActive { get; set; } = true;

    // --- Shopify çift yönlü senkron alanları (offer düzeyinde) ---
    public long? ShopifyProductId { get; set; }
    public string Source { get; set; } = ProductSource.Marketplace;
    public DateTimeOffset? LastSyncedAt { get; set; }
}

public static class ProductSource
{
    public const string Marketplace = "marketplace";
    public const string Shopify = "shopify";
}
