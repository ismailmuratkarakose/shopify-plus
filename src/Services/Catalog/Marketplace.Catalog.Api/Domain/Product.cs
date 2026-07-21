using Marketplace.BuildingBlocks.Domain;

namespace Marketplace.Catalog.Api.Domain;

public class Product : AuditableTenantEntity
{
    public string Sku { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public Guid? CategoryId { get; set; }

    public decimal Price { get; set; }
    public string Currency { get; set; } = "TRY";

    public bool IsActive { get; set; } = true;

    // --- Shopify çift yönlü senkron alanları ---
    // Çakışma çözümünde kaynak-önceliği ve idempotency için kullanılır.
    public long? ShopifyProductId { get; set; }
    public string Source { get; set; } = ProductSource.Marketplace;
    public DateTimeOffset? LastSyncedAt { get; set; }
}

public static class ProductSource
{
    public const string Marketplace = "marketplace";
    public const string Shopify = "shopify";
}
