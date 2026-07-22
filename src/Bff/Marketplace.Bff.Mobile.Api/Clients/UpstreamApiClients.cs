using System.Net;
using System.Net.Http.Json;

namespace Marketplace.Bff.Mobile.Api.Clients;

// System.Net.Http.Json Web (camelCase) varsayılanını kullanır; ek konfig gerekmez.

// --- Catalog (master + satıcı teklifleri) ---
public interface ICatalogApi
{
    Task<IReadOnlyList<ProductListItem>> GetProductsAsync(int page, int pageSize, string? search, CancellationToken ct);
    Task<ProductWithOffers?> GetProductAsync(Guid id, CancellationToken ct);
}

public sealed class CatalogApiClient(HttpClient http) : ICatalogApi
{
    public async Task<IReadOnlyList<ProductListItem>> GetProductsAsync(int page, int pageSize, string? search, CancellationToken ct)
    {
        var url = $"/api/products?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        return await http.GetFromJsonAsync<List<ProductListItem>>(url, ct) ?? [];
    }

    public async Task<ProductWithOffers?> GetProductAsync(Guid id, CancellationToken ct)
    {
        using var resp = await http.GetAsync($"/api/products/{id}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<ProductWithOffers>(ct);
    }
}

// --- Order ---
public interface IOrderApi
{
    Task<UpstreamOrder?> CreateAsync(CreateOrderUpstream body, CancellationToken ct);
    Task<IReadOnlyList<UpstreamOrder>> GetMyPurchasesAsync(int page, int pageSize, CancellationToken ct);
    Task<UpstreamOrder?> GetMyPurchaseAsync(Guid id, CancellationToken ct);
}

public sealed class OrderApiClient(HttpClient http) : IOrderApi
{
    public async Task<UpstreamOrder?> CreateAsync(CreateOrderUpstream body, CancellationToken ct)
    {
        using var resp = await http.PostAsJsonAsync("/api/orders", body, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<UpstreamOrder>(ct);
    }

    public async Task<IReadOnlyList<UpstreamOrder>> GetMyPurchasesAsync(int page, int pageSize, CancellationToken ct)
        => await http.GetFromJsonAsync<List<UpstreamOrder>>($"/api/orders/purchases?page={page}&pageSize={pageSize}", ct) ?? [];

    public async Task<UpstreamOrder?> GetMyPurchaseAsync(Guid id, CancellationToken ct)
    {
        using var resp = await http.GetAsync($"/api/orders/purchases/{id}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<UpstreamOrder>(ct);
    }
}
