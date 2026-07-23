using Marketplace.BuildingBlocks.Domain;

namespace Marketplace.ShopifySync.Api.Domain;

/// <summary>
/// Shopify'dan senkronlanan ürün (read-model). Shopify kaynak sistemdir; bu kayıt yalnızca okuma/
/// gösterim/kişiselleştirme içindir. Tenant = Shopify mağazası.
/// </summary>
public class SyncedProduct : AuditableStoreEntity
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
public class SyncedCollection : AuditableStoreEntity
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
public class SyncedOrder : AuditableStoreEntity
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

/// <summary>Shopify indirimi/kampanyası (read-model) — kampanya alanlarına bağlanır.</summary>
public class SyncedDiscount : AuditableStoreEntity
{
    public long ShopifyDiscountId { get; set; }
    public string Title { get; set; } = default!;
    public string Code { get; set; } = default!;
    public string DiscountType { get; set; } = default!;   // percentage / fixed_amount
    public decimal Value { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public string Status { get; set; } = default!;          // active / expired / scheduled
    public int UsageCount { get; set; }
}

/// <summary>Shopify içerik sayfası (read-model) — mobil içerik ekranlarında gösterilir.</summary>
public class SyncedPage : AuditableStoreEntity
{
    public long ShopifyPageId { get; set; }
    public string Title { get; set; } = default!;
    public string Handle { get; set; } = default!;
    public string? BodyHtml { get; set; }
    public string Status { get; set; } = default!;
    public DateTimeOffset ShopifyUpdatedAt { get; set; }
}

/// <summary>
/// Mağaza başına senkron durumu: son çalışma zamanı, sonuç sayıları ve varsa hata.
/// Yönetim panelinde "veriler ne kadar güncel" sorusunu yanıtlar.
/// </summary>
public class StoreSyncState : AuditableStoreEntity
{
    public DateTimeOffset? LastSyncAt { get; set; }
    public string LastStatus { get; set; } = "never";       // success / failed / never
    public string? LastError { get; set; }
    public long DurationMs { get; set; }
    public int ProductCount { get; set; }
    public int CollectionCount { get; set; }
    public int OrderCount { get; set; }
    public int CustomerCount { get; set; }
    public int DiscountCount { get; set; }
    public int PageCount { get; set; }
    /// <summary>Senkronu tetikleyen: manual / reconciliation</summary>
    public string LastTrigger { get; set; } = "manual";
}

/// <summary>Shopify müşterisi (read-model) — segmentasyon ve kişiselleştirme için.</summary>
public class SyncedCustomer : AuditableStoreEntity
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
