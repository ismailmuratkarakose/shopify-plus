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

    public Task<IReadOnlyList<ShopifyCustomerData>> GetCustomersAsync(ShopifyStoreCredentials store, CancellationToken ct)
    {
        var people = new[] { ("Ayşe", "Yılmaz"), ("Mehmet", "Demir"), ("Zeynep", "Kaya") };
        var customers = people.Select((p, i) => new ShopifyCustomerData(
            DeterministicId(store.ShopDomain, $"C{i}"),
            $"{p.Item1.ToLowerInvariant()}@example.com", p.Item1, p.Item2, $"+9055500000{i:D2}",
            OrdersCount: i + 1, TotalSpent: 250m + i * 175,
            DateTimeOffset.UtcNow.AddMonths(-6 + i), DateTimeOffset.UtcNow.AddDays(-i)))
            .ToList();
        _logger.LogInformation("[SIMULATOR] {Store} için {Count} müşteri üretildi.", store.ShopDomain, customers.Count);
        return Task.FromResult<IReadOnlyList<ShopifyCustomerData>>(customers);
    }

    public async Task<IReadOnlyList<ShopifyOrderData>> GetOrdersAsync(ShopifyStoreCredentials store, CancellationToken ct)
    {
        var products = await GetProductsAsync(store, ct);
        var customers = await GetCustomersAsync(store, ct);
        var statuses = new[] { ("paid", "fulfilled"), ("paid", (string?)null), ("pending", null), ("refunded", "fulfilled") };

        var orders = new List<ShopifyOrderData>();
        for (var i = 0; i < statuses.Length; i++)
        {
            var customer = customers[i % customers.Count];
            var product = products[i % products.Count];
            var variant = product.Variants[0];
            var qty = 1 + (i % 3);
            var line = new ShopifyOrderLineData(
                DeterministicId(store.ShopDomain, $"L{i}"), product.ProductId, variant.VariantId,
                variant.Sku, product.Title, qty, variant.Price);

            orders.Add(new ShopifyOrderData(
                DeterministicId(store.ShopDomain, $"O{i}"), $"#{1001 + i}",
                customer.CustomerId, customer.Email,
                statuses[i].Item1, statuses[i].Item2,
                variant.Price * qty, "TRY",
                DateTimeOffset.UtcNow.AddDays(-10 + i), DateTimeOffset.UtcNow.AddDays(-i),
                new[] { line }));
        }
        _logger.LogInformation("[SIMULATOR] {Store} için {Count} sipariş üretildi.", store.ShopDomain, orders.Count);
        return orders;
    }
}
