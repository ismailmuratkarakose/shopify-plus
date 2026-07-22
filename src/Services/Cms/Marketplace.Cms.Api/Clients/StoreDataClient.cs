using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Net.Http.Headers;

namespace Marketplace.Cms.Api.Clients;

/// <summary>İçerik doğrulamasında kullanılan mağaza referansları (Shopify read-model'inden).</summary>
public record StoreRefs(HashSet<long> ProductIds, HashSet<long> CollectionIds, HashSet<string> DiscountCodes);

public interface IStoreDataClient
{
    /// <summary>Mağazanın güncel ürün/koleksiyon/indirim kimliklerini çeker. Servise ulaşılamazsa null.</summary>
    Task<StoreRefs?> GetRefsAsync(CancellationToken ct);
}

/// <summary>Gelen kullanıcının JWT'sini downstream çağrılara taşır (kiracı kapsamı korunur).</summary>
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

public sealed class StoreDataClient(HttpClient http, ILogger<StoreDataClient> logger) : IStoreDataClient
{
    public async Task<StoreRefs?> GetRefsAsync(CancellationToken ct)
    {
        try
        {
            var products = await ReadIdsAsync("/api/shopify/products?pageSize=100", "productId", ct);
            var collections = await ReadIdsAsync("/api/shopify/collections", "collectionId", ct);
            var codes = await ReadStringsAsync("/api/shopify/discounts", "code", ct);
            return new StoreRefs(products, collections, codes);
        }
        catch (Exception ex)
        {
            // Doğrulama altyapısal nedenle yapılamadıysa içerik akışını durdurmayız (uyarı üretilir).
            logger.LogWarning(ex, "Mağaza referansları alınamadı; içerik doğrulaması atlanıyor.");
            return null;
        }
    }

    private async Task<HashSet<long>> ReadIdsAsync(string url, string property, CancellationToken ct)
    {
        var set = new HashSet<long>();
        using var resp = await http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        foreach (var el in doc.RootElement.EnumerateArray())
            if (el.TryGetProperty(property, out var v) && v.TryGetInt64(out var id))
                set.Add(id);
        return set;
    }

    private async Task<HashSet<string>> ReadStringsAsync(string url, string property, CancellationToken ct)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var resp = await http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        foreach (var el in doc.RootElement.EnumerateArray())
            if (el.TryGetProperty(property, out var v) && v.GetString() is { } s)
                set.Add(s);
        return set;
    }
}
