using System.Text.Json.Nodes;

namespace Marketplace.Mobile.Api.Clients;

/// <summary>
/// Ortak katalog istemcisi (R4 dilim 2): mobil katalog artık mağaza-kapsamlı Shopify read-model'ini
/// değil, pazaryerinin ortak kataloğunu okur (barkod master + satıcı teklifleri). Kamusal uçlar
/// anonim olduğundan kimlik taşınmaz.
/// </summary>
public interface ICatalogClient
{
    Task<JsonNode?> SearchAsync(string? q, Guid? categoryId, string? sort, int page, int pageSize,
        CancellationToken ct);
    Task<JsonNode?> GetProductAsync(Guid productId, CancellationToken ct);
    Task<JsonNode?> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct);
    Task<JsonNode?> GetCategoriesAsync(CancellationToken ct);
}

public sealed class CatalogClient(HttpClient http, ILogger<CatalogClient> logger) : ICatalogClient
{
    public async Task<JsonNode?> SearchAsync(string? q, Guid? categoryId, string? sort, int page,
        int pageSize, CancellationToken ct)
    {
        var query = $"?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(q)) query += $"&q={Uri.EscapeDataString(q)}";
        if (categoryId is { } cid) query += $"&categoryId={cid}";
        if (!string.IsNullOrWhiteSpace(sort)) query += $"&sort={Uri.EscapeDataString(sort)}";
        return await GetAsync($"/api/catalog/products{query}", ct);
    }

    public Task<JsonNode?> GetProductAsync(Guid productId, CancellationToken ct)
        => GetAsync($"/api/catalog/products/{productId}", ct);

    public Task<JsonNode?> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
        => ids.Count == 0
            ? Task.FromResult<JsonNode?>(new JsonObject { ["total"] = 0, ["items"] = new JsonArray() })
            : GetAsync($"/api/catalog/products?ids={string.Join(",", ids)}&pageSize={Math.Min(ids.Count, 100)}", ct);

    public Task<JsonNode?> GetCategoriesAsync(CancellationToken ct)
        => GetAsync("/api/catalog/categories", ct);

    private async Task<JsonNode?> GetAsync(string url, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Katalog çağrısı başarısız: {Url}", url);
            return null;
        }
    }
}
