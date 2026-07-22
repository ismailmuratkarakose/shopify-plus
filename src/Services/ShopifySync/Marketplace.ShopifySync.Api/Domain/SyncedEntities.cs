using Marketplace.BuildingBlocks.Domain;

namespace Marketplace.ShopifySync.Api.Domain;

/// <summary>
/// Shopify'dan senkronlanan ürün (read-model). Shopify kaynak sistemdir; bu kayıt yalnızca okuma/
/// gösterim/kişiselleştirme içindir. Tenant = Shopify mağazası.
/// </summary>
public class SyncedProduct : AuditableTenantEntity
{
    public long ShopifyProductId { get; set; }
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public string? Vendor { get; set; }
    public string? ProductType { get; set; }
    public string Handle { get; set; } = default!;
    public string Status { get; set; } = "active";
    public string? ImageUrl { get; set; }
    public DateTimeOffset ShopifyUpdatedAt { get; set; }

    public List<SyncedVariant> Variants { get; set; } = [];
}

public class SyncedVariant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SyncedProductId { get; set; }
    public long ShopifyVariantId { get; set; }
    public string? Sku { get; set; }
    public string? Barcode { get; set; }
    public decimal Price { get; set; }
    public decimal? CompareAtPrice { get; set; }
    public int InventoryQuantity { get; set; }
    public string? Title { get; set; }
}

/// <summary>Shopify koleksiyonu (read-model) + ürün üyelikleri.</summary>
public class SyncedCollection : AuditableTenantEntity
{
    public long ShopifyCollectionId { get; set; }
    public string Title { get; set; } = default!;
    public string Handle { get; set; } = default!;

    public List<SyncedCollectionProduct> Products { get; set; } = [];
}

public class SyncedCollectionProduct
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SyncedCollectionId { get; set; }
    public long ShopifyProductId { get; set; }
}

/// <summary>
/// Shopify siparişi (read-model). Checkout/ödeme Shopify'da yürür; burada SALT OKUNUR tutulur
/// (sipariş geçmişi, analitik, kişiselleştirme).
/// </summary>
public class SyncedOrder : AuditableTenantEntity
{
    public long ShopifyOrderId { get; set; }
    public string Name { get; set; } = default!;            // ör. #1001
    public long? ShopifyCustomerId { get; set; }
    public string? Email { get; set; }
    public string FinancialStatus { get; set; } = default!;  // paid / pending / refunded ...
    public string? FulfillmentStatus { get; set; }           // fulfilled / null ...
    public decimal TotalPrice { get; set; }
    public string Currency { get; set; } = default!;
    public DateTimeOffset ShopifyCreatedAt { get; set; }
    public DateTimeOffset ShopifyUpdatedAt { get; set; }

    public List<SyncedOrderLine> Lines { get; set; } = [];
}

public class SyncedOrderLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SyncedOrderId { get; set; }
    public long ShopifyLineId { get; set; }
    public long ShopifyProductId { get; set; }
    public long ShopifyVariantId { get; set; }
    public string? Sku { get; set; }
    public string Title { get; set; } = default!;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}

/// <summary>Shopify müşterisi (read-model) — segmentasyon ve kişiselleştirme için.</summary>
public class SyncedCustomer : AuditableTenantEntity
{
    public long ShopifyCustomerId { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }
    public int OrdersCount { get; set; }
    public decimal TotalSpent { get; set; }
    public DateTimeOffset ShopifyCreatedAt { get; set; }
    public DateTimeOffset ShopifyUpdatedAt { get; set; }
}
