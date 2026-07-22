using System.Net;
using System.Net.Http.Json;

namespace Marketplace.Bff.Mobile.Api.Clients;

// System.Net.Http.Json varsayılan olarak Web (camelCase, case-insensitive) ayarlarını kullanır;
// upstream servisler ASP.NET varsayılanı camelCase döndürdüğü için ek konfigürasyon gerekmez.

// --- Catalog ---
public interface ICatalogApi
{
    Task<IReadOnlyList<CatalogProduct>> GetProductsAsync(int page, int pageSize, CancellationToken ct);
    Task<CatalogProduct?> GetProductAsync(Guid id, CancellationToken ct);
}

public sealed class CatalogApiClient(HttpClient http) : ICatalogApi
{
    public async Task<IReadOnlyList<CatalogProduct>> GetProductsAsync(int page, int pageSize, CancellationToken ct)
        => await http.GetFromJsonAsync<List<CatalogProduct>>($"/api/products?page={page}&pageSize={pageSize}", ct)
           ?? [];

    public async Task<CatalogProduct?> GetProductAsync(Guid id, CancellationToken ct)
    {
        using var resp = await http.GetAsync($"/api/products/{id}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<CatalogProduct>(ct);
    }
}

// --- Inventory ---
public interface IInventoryApi
{
    Task<IReadOnlyList<InventoryItem>> GetAllAsync(CancellationToken ct);
}

public sealed class InventoryApiClient(HttpClient http) : IInventoryApi
{
    public async Task<IReadOnlyList<InventoryItem>> GetAllAsync(CancellationToken ct)
        => await http.GetFromJsonAsync<List<InventoryItem>>("/api/inventory", ct) ?? [];
}

// --- Order ---
public interface IOrderApi
{
    Task<UpstreamOrder?> CreateAsync(CreateOrderUpstream body, CancellationToken ct);
    Task<IReadOnlyList<UpstreamOrder>> GetOrdersAsync(int page, int pageSize, CancellationToken ct);
    Task<UpstreamOrder?> GetOrderAsync(Guid id, CancellationToken ct);
}

public sealed class OrderApiClient(HttpClient http) : IOrderApi
{
    public async Task<UpstreamOrder?> CreateAsync(CreateOrderUpstream body, CancellationToken ct)
    {
        using var resp = await http.PostAsJsonAsync("/api/orders", body, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<UpstreamOrder>(ct);
    }

    public async Task<IReadOnlyList<UpstreamOrder>> GetOrdersAsync(int page, int pageSize, CancellationToken ct)
        => await http.GetFromJsonAsync<List<UpstreamOrder>>($"/api/orders?page={page}&pageSize={pageSize}", ct)
           ?? [];

    public async Task<UpstreamOrder?> GetOrderAsync(Guid id, CancellationToken ct)
    {
        using var resp = await http.GetAsync($"/api/orders/{id}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<UpstreamOrder>(ct);
    }
}
