using Marketplace.ShopifySync.Api.Domain;

namespace Marketplace.ShopifySync.Api.Catalog;

public record CatalogFeedItem(string? Barcode, string Title, decimal Price, int StockQuantity,
    string? Description, string? Brand, string? ImageUrl, string? Sku, decimal? CompareAtPrice,
    long ShopifyProductId, long ShopifyVariantId);

public record CatalogFeedRequest(Guid StoreId, List<CatalogFeedItem> Items);

public interface ICatalogFeedClient
{
    /// <summary>Senkronlanan ürünleri ortak kataloğa iter. Başarısızlık senkronu DÜŞÜRMEZ (loglanır).</summary>
    Task FeedAsync(Guid storeId, IReadOnlyList<SyncedProduct> products, CancellationToken ct);
}

/// <summary>
/// Shopify → Katalog besleyicisi (R4). Shopify artık son nokta değil, ortak kataloğun bir KAYNAĞIDIR:
/// barkodlu her varyant kataloğa mağazanın teklifi olarak itilir (source=shopify). Barkodsuz varyant
/// master'a bağlanamayacağı için atlanır ve loglanır — mağaza barkodları Shopify'da doldurmalıdır.
/// </summary>
public sealed class CatalogFeedClient(HttpClient http, ILogger<CatalogFeedClient> logger) : ICatalogFeedClient
{
    public async Task FeedAsync(Guid storeId, IReadOnlyList<SyncedProduct> products, CancellationToken ct)
    {
        var items = new List<CatalogFeedItem>();
        var skippedNoBarcode = 0;

        foreach (var p in products.Where(p => p.Status == "active"))
        {
            foreach (var v in p.Variants)
            {
                if (string.IsNullOrWhiteSpace(v.Barcode)) { skippedNoBarcode++; continue; }

                // Varyantlı üründe kart adı "Ürün - Varyant" olur (her barkod ayrı karttır).
                var title = string.IsNullOrWhiteSpace(v.Title) || v.Title == "Default Title"
                    ? p.Title
                    : $"{p.Title} - {v.Title}";

                items.Add(new CatalogFeedItem(v.Barcode, title, v.Price, v.InventoryQuantity,
                    p.Description, p.Vendor, p.ImageUrl, v.Sku, v.CompareAtPrice,
                    p.ShopifyProductId, v.ShopifyVariantId));
            }
        }

        try
        {
            using var resp = await http.PostAsJsonAsync("/internal/feed/shopify",
                new CatalogFeedRequest(storeId, items), ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Katalog beslemesi reddedildi: store={Store} durum={Status}",
                    storeId, (int)resp.StatusCode);
                return;
            }
            logger.LogInformation(
                "Katalog beslendi: store={Store} gönderilen={Sent} barkodsuz atlanan={Skipped}",
                storeId, items.Count, skippedNoBarcode);
        }
        catch (Exception ex)
        {
            // Besleme senkronu düşürmez: Shopify read-model'i günceldir, katalog bir sonraki
            // senkronda (mutabakat) yakalar.
            logger.LogWarning(ex, "Katalog beslemesi başarısız: store={Store}", storeId);
        }
    }
}
