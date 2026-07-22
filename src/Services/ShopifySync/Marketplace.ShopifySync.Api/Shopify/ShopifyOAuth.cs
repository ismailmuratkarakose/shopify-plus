using System.Net.Http.Json;
using System.Text.Json;

namespace Marketplace.ShopifySync.Api.Shopify;

/// <summary>
/// Shopify OAuth (mağaza bağlama) soyutlaması. `Shopify:ClientMode` ile simulator/graphql seçilir.
/// - simulator: dış çağrı yapmadan sahte token üretir (lokal geliştirme).
/// - graphql: gerçek authorize URL + code→access_token değişimi.
/// </summary>
public interface IShopifyOAuth
{
    bool IsSimulator { get; }

    /// <summary>Mağaza sahibinin yönlendirileceği Shopify authorize URL'i (graphql modunda gerçek).</summary>
    string BuildInstallUrl(string shop, string state);

    /// <summary>Callback'te dönen code'u access token ile değişir.</summary>
    Task<string> ExchangeCodeAsync(string shop, string code, CancellationToken ct);
}

public static class ShopDomain
{
    /// <summary>"acme" veya "acme.myshopify.com" → "acme.myshopify.com".</summary>
    public static string Normalize(string shop)
    {
        shop = shop.Trim().ToLowerInvariant().Replace("https://", "").Replace("http://", "").TrimEnd('/');
        return shop.Contains('.') ? shop : $"{shop}.myshopify.com";
    }
}

public sealed class SimulatorShopifyOAuth : IShopifyOAuth
{
    public bool IsSimulator => true;

    public string BuildInstallUrl(string shop, string state)
        => $"https://{ShopDomain.Normalize(shop)}/admin/oauth/authorize?simulator=1&state={state}";

    public Task<string> ExchangeCodeAsync(string shop, string code, CancellationToken ct)
        => Task.FromResult($"shpat_sim_{Guid.NewGuid():N}");
}

public sealed class GraphQlShopifyOAuth : IShopifyOAuth
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;

    public GraphQlShopifyOAuth(HttpClient http, IConfiguration config)
    {
        _http = http;
        _config = config;
    }

    public bool IsSimulator => false;

    public string BuildInstallUrl(string shop, string state)
    {
        var s = ShopDomain.Normalize(shop);
        var key = _config["Shopify:ApiKey"];
        var scopes = _config["Shopify:Scopes"] ?? "read_products,read_orders,read_customers";
        var redirect = $"{_config["Shopify:AppBaseUrl"]}/shopify/oauth/callback";
        return $"https://{s}/admin/oauth/authorize?client_id={key}&scope={Uri.EscapeDataString(scopes)}" +
               $"&redirect_uri={Uri.EscapeDataString(redirect)}&state={Uri.EscapeDataString(state)}";
    }

    public async Task<string> ExchangeCodeAsync(string shop, string code, CancellationToken ct)
    {
        var s = ShopDomain.Normalize(shop);
        using var resp = await _http.PostAsJsonAsync($"https://{s}/admin/oauth/access_token", new
        {
            client_id = _config["Shopify:ApiKey"],
            client_secret = _config["Shopify:ApiSecret"],
            code
        }, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }
}
