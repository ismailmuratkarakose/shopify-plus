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
}
