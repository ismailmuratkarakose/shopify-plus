using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Net.Http.Headers;

namespace Marketplace.Cms.Api.Clients;

/// <summary>
/// İçerik doğrulamasında kullanılan referanslar (R4 dilim 2): ürün ve kategori kimlikleri ORTAK
/// KATALOG'dan (Guid, metin olarak), indirim kodları Shopify read-model'inden gelir.
/// </summary>
public record StoreRefs(HashSet<string> ProductIds, HashSet<string> CategoryIds, HashSet<string> DiscountCodes);

public interface IStoreDataClient
{
    /// <summary>Güncel referans kümelerini çeker. Servislere ulaşılamazsa null (doğrulama atlanır, uyarı üretilir).</summary>
    Task<StoreRefs?> GetRefsAsync(CancellationToken ct);
}

/// <summary>Gelen kullanıcının JWT'sini downstream çağrılara taşır (Shopify indirim okuması için).</summary>
public sealed class AuthForwardingHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var incoming = accessor.HttpContext?.Request.Headers[HeaderNames.Authorization].ToString();
        if (!string.IsNullOrWhiteSpace(incoming) && AuthenticationHeaderValue.TryParse(incoming, out var parsed))
            request.Headers.Authorization = parsed;
        return base.SendAsync(request, ct);
    }
}

public sealed class StoreDataClient(IHttpClientFactory factory, ILogger<StoreDataClient> logger) : IStoreDataClient
{
    /// <summary>Doğrulamada taranan en çok kart sayısı (sayfa başına 100 × 10 sayfa).</summary>
    private const int MaxProductPages = 10;

    public async Task<StoreRefs?> GetRefsAsync(CancellationToken ct)
    {
        try
        {
            var catalog = factory.CreateClient("catalog");
            var shopify = factory.CreateClient("shopifysync");

            var products = await ReadCatalogProductIdsAsync(catalog, ct);
            var categories = await ReadStringsAsync(catalog, "/api/catalog/categories", "id", ct);
            var codes = await ReadStringsAsync(shopify, "/api/shopify/discounts", "code", ct);
            return new StoreRefs(products, categories, codes);
        }
        catch (Exception ex)
        {
            // Doğrulama altyapısal nedenle yapılamadıysa içerik akışını durdurmayız (uyarı üretilir).
            logger.LogWarning(ex, "Referanslar alınamadı; içerik doğrulaması atlanıyor.");
            return null;
        }
    }

    private static async Task<HashSet<string>> ReadCatalogProductIdsAsync(HttpClient catalog, CancellationToken ct)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var page = 1; page <= MaxProductPages; page++)
        {
            using var resp = await catalog.GetAsync($"/api/catalog/products?page={page}&pageSize=100", ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var items = doc.RootElement.GetProperty("items");
            foreach (var el in items.EnumerateArray())
                if (el.TryGetProperty("productId", out var v) && v.GetString() is { } s)
                    set.Add(s);
            var total = doc.RootElement.GetProperty("total").GetInt32();
            if (page * 100 >= total) break;
        }
        return set;
    }

    private static async Task<HashSet<string>> ReadStringsAsync(HttpClient http, string url, string property,
        CancellationToken ct)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var resp = await http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        foreach (var el in doc.RootElement.EnumerateArray())
            if (el.TryGetProperty(property, out var v) && v.ToString() is { Length: > 0 } s)
                set.Add(s);
        return set;
    }
}
