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
}
