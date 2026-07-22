using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Net.Http.Headers;

namespace Marketplace.Mobile.Api.Clients;

// --- Shopify read-model sözleşmeleri (ShopifySync servisinden) ---
public record StoreVariant(long VariantId, string? Sku, string? Barcode, decimal Price,
    decimal? CompareAtPrice, int InventoryQuantity, string? Title);
public record StoreProduct(long ProductId, string Title, string? Description, string? Vendor,
    string? ProductType, string Handle, string Status, string? ImageUrl, List<StoreVariant> Variants);
public record StoreCollection(long CollectionId, string Title, string Handle, List<long> ProductIds);
public record StoreIntegration(string ShopDomain, bool IsActive, bool TokenStored);

/// <summary>Gelen kullanıcının JWT'sini downstream servislere taşır (kiracı kapsamı korunur).</summary>
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

// --- CMS: yayınlanan deneyim anlık görüntüsü ---
public record SnapshotResponse(string? Json, string? ETag, bool NotModified);

public interface IExperienceClient
{
    Task<SnapshotResponse?> GetCurrentAsync(string? ifNoneMatch, CancellationToken ct);
}

public sealed class ExperienceClient(HttpClient http, ILogger<ExperienceClient> logger) : IExperienceClient
{
    public async Task<SnapshotResponse?> GetCurrentAsync(string? ifNoneMatch, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "/api/experience/current");
            if (!string.IsNullOrEmpty(ifNoneMatch))
                req.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);

            using var resp = await http.SendAsync(req, ct);
            if (resp.StatusCode == HttpStatusCode.NotModified)
                return new SnapshotResponse(null, ifNoneMatch, true);
            if (resp.StatusCode == HttpStatusCode.NotFound)
                return null;

            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            return new SnapshotResponse(json, resp.Headers.ETag?.ToString(), false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Deneyim anlık görüntüsü alınamadı.");
            return null;
        }
    }
}

// --- ShopifySync: mağaza verisi ---
public interface IStoreClient
{
    Task<IReadOnlyList<StoreProduct>> GetProductsAsync(CancellationToken ct);
    Task<StoreProduct?> GetProductAsync(long productId, CancellationToken ct);
    Task<IReadOnlyList<StoreCollection>> GetCollectionsAsync(CancellationToken ct);
    Task<StoreIntegration?> GetIntegrationAsync(CancellationToken ct);
}

public sealed class StoreClient(HttpClient http) : IStoreClient
{
    public async Task<IReadOnlyList<StoreProduct>> GetProductsAsync(CancellationToken ct)
        => await http.GetFromJsonAsync<List<StoreProduct>>("/api/shopify/products?pageSize=100", ct) ?? [];

    public async Task<StoreProduct?> GetProductAsync(long productId, CancellationToken ct)
    {
        using var resp = await http.GetAsync($"/api/shopify/products/{productId}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<StoreProduct>(ct);
    }

    public async Task<IReadOnlyList<StoreCollection>> GetCollectionsAsync(CancellationToken ct)
        => await http.GetFromJsonAsync<List<StoreCollection>>("/api/shopify/collections", ct) ?? [];

    public async Task<StoreIntegration?> GetIntegrationAsync(CancellationToken ct)
    {
        using var resp = await http.GetAsync("/api/shopify/integration", ct);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<StoreIntegration>(ct);
    }
}
