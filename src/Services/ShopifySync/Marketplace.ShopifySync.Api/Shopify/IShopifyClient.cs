namespace Marketplace.ShopifySync.Api.Shopify;

/// <summary>Bir merchant'ın Shopify mağaza kimlik bilgileri (kullanım anında çözülür).</summary>
public record ShopifyStoreCredentials(string ShopDomain, string AccessToken);

/// <summary>Shopify'a push edilecek ürün.</summary>
public record ShopifyProductPush(string Sku, string Title, string? Description, decimal Price, string Currency);

public record ShopifyProductRef(long ShopifyProductId);

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
}
