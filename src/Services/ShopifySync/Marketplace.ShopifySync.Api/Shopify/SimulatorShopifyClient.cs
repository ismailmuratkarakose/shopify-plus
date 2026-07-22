using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Marketplace.ShopifySync.Api.Shopify;

/// <summary>
/// Gerçek Shopify mağazası olmadan lokal geliştirme/test için. Push'u loglar ve
/// SKU'dan deterministik sahte bir ShopifyProductId üretir (tekrar push aynı id'yi verir).
/// </summary>
public sealed class SimulatorShopifyClient : IShopifyClient
{
    private readonly ILogger<SimulatorShopifyClient> _logger;

    public SimulatorShopifyClient(ILogger<SimulatorShopifyClient> logger) => _logger = logger;

    public Task<ShopifyProductRef> UpsertProductAsync(
        ShopifyStoreCredentials store,
        ShopifyProductPush product,
        long? existingShopifyProductId,
        CancellationToken ct)
    {
        var id = existingShopifyProductId ?? DeterministicId(store.ShopDomain, product.Sku);
        _logger.LogInformation(
            "[SIMULATOR] Shopify {Op}: store={Store} sku={Sku} title={Title} price={Price} {Cur} -> shopifyProductId={Id}",
            existingShopifyProductId is null ? "productCreate" : "productUpdate",
            store.ShopDomain, product.Sku, product.Title, product.Price, product.Currency, id);
        return Task.FromResult(new ShopifyProductRef(id));
    }

    private static long DeterministicId(string shopDomain, string sku)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{shopDomain}|{sku}"));
        // İlk 6 byte → pozitif long (Shopify id benzeri).
        long id = 0;
        for (var i = 0; i < 6; i++) id = (id << 8) | bytes[i];
        return id;
    }

    // --- Read (senkron): mağazaya göre deterministik sahte katalog üretir ---

    public Task<IReadOnlyList<ShopifyProductData>> GetProductsAsync(ShopifyStoreCredentials store, CancellationToken ct)
    {
        var titles = new[] { "Kablosuz Kulaklık", "Akıllı Saat", "Bluetooth Hoparlör", "Dizüstü Çanta", "USB-C Kablo" };
        var products = new List<ShopifyProductData>();
        for (var i = 0; i < titles.Length; i++)
        {
            var pid = DeterministicId(store.ShopDomain, $"P{i}");
            var vid = DeterministicId(store.ShopDomain, $"V{i}");
            var price = 100m + i * 50;
            var variant = new ShopifyVariantData(
                vid, $"SKU-{i:D3}", $"869000000{i:D4}", price, price + 30, 10 + i * 5, "Standart");
            products.Add(new ShopifyProductData(
                pid, titles[i], $"{titles[i]} açıklaması", "MarkaX", "Elektronik",
                titles[i].ToLowerInvariant().Replace(' ', '-'), "active", null,
                DateTimeOffset.UtcNow.AddDays(-i), new[] { variant }));
        }
        _logger.LogInformation("[SIMULATOR] {Store} için {Count} ürün üretildi.", store.ShopDomain, products.Count);
        return Task.FromResult<IReadOnlyList<ShopifyProductData>>(products);
    }

    public async Task<IReadOnlyList<ShopifyCollectionData>> GetCollectionsAsync(ShopifyStoreCredentials store, CancellationToken ct)
    {
        var products = await GetProductsAsync(store, ct);
        var ids = products.Select(p => p.ProductId).ToList();
        var collections = new List<ShopifyCollectionData>
        {
            new(DeterministicId(store.ShopDomain, "C-all"), "Tüm Ürünler", "tum-urunler", ids),
            new(DeterministicId(store.ShopDomain, "C-featured"), "Öne Çıkanlar", "one-cikanlar", ids.Take(2).ToList())
        };
        return collections;
    }
}
