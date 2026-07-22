namespace Marketplace.ShopifySync.Api.Shopify;

/// <summary>Bir merchant'ın Shopify mağaza kimlik bilgileri (kullanım anında çözülür).</summary>
public record ShopifyStoreCredentials(string ShopDomain, string AccessToken);

/// <summary>Shopify'a push edilecek ürün.</summary>
public record ShopifyProductPush(string Sku, string Title, string? Description, decimal Price, string Currency);

public record ShopifyProductRef(long ShopifyProductId);

// --- Shopify'dan OKUNAN veri (read-model beslemesi; Shopify kaynak sistem) ---
public record ShopifyVariantData(
    long VariantId, string? Sku, string? Barcode, decimal Price, decimal? CompareAtPrice,
    int InventoryQuantity, string? Title);

public record ShopifyProductData(
    long ProductId, string Title, string? Description, string? Vendor, string? ProductType,
    string Handle, string Status, string? ImageUrl, DateTimeOffset UpdatedAt,
    IReadOnlyList<ShopifyVariantData> Variants);

public record ShopifyCollectionData(long CollectionId, string Title, string Handle, IReadOnlyList<long> ProductIds);

public record ShopifyOrderLineData(
    long LineId, long ProductId, long VariantId, string? Sku, string Title, int Quantity, decimal Price);

public record ShopifyOrderData(
    long OrderId, string Name, long? CustomerId, string? Email,
    string FinancialStatus, string? FulfillmentStatus, decimal TotalPrice, string Currency,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, IReadOnlyList<ShopifyOrderLineData> Lines);

public record ShopifyCustomerData(
    long CustomerId, string? Email, string? FirstName, string? LastName, string? Phone,
    int OrdersCount, decimal TotalSpent, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>
/// Shopify Admin API soyutlaması. Transport (GraphQL/REST/simulator) implementasyonlarda gizlenir.
/// Config `Shopify:ClientMode` ile seçilir.
/// </summary>
public interface IShopifyClient
{
    /// <summary>Ürünü Shopify'da oluşturur/günceller. existingShopifyProductId null ise create.</summary>
    Task<ShopifyProductRef> UpsertProductAsync(
        ShopifyStoreCredentials store,
        ShopifyProductPush product,
        long? existingShopifyProductId,
        CancellationToken ct);

    /// <summary>Mağazanın ürünlerini (varyant/fiyat/stok/barkod dahil) çeker — read-model senkronu için.</summary>
    Task<IReadOnlyList<ShopifyProductData>> GetProductsAsync(ShopifyStoreCredentials store, CancellationToken ct);

    /// <summary>Mağazanın koleksiyonlarını (ürün üyelikleriyle) çeker.</summary>
    Task<IReadOnlyList<ShopifyCollectionData>> GetCollectionsAsync(ShopifyStoreCredentials store, CancellationToken ct);

    /// <summary>Mağazanın siparişlerini (satırlarıyla) çeker. Checkout Shopify'da olduğu için siparişler salt-okunur.</summary>
    Task<IReadOnlyList<ShopifyOrderData>> GetOrdersAsync(ShopifyStoreCredentials store, CancellationToken ct);

    /// <summary>Mağazanın müşterilerini çeker (segmentasyon/kişiselleştirme için).</summary>
    Task<IReadOnlyList<ShopifyCustomerData>> GetCustomersAsync(ShopifyStoreCredentials store, CancellationToken ct);
}
