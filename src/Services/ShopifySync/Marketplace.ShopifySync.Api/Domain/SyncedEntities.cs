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
