using System.Net.Http.Json;

namespace Marketplace.ShopifySync.Api.Shopify;

/// <summary>
/// Merchant servisinin internal write endpoint'ine entegrasyon config'i kaydeder (server-to-server).
/// Merchant kaynağı şifreler ve MerchantIntegrationConfigured yayınlar → read-model consumer'ı senkronlar.
/// </summary>
public interface IMerchantIntegrationWriter
{
    Task SaveShopifyAsync(Guid merchantId, string shopDomain, string accessToken, CancellationToken ct);
}

public sealed class MerchantIntegrationWriter(HttpClient http) : IMerchantIntegrationWriter
{
    public async Task SaveShopifyAsync(Guid merchantId, string shopDomain, string accessToken, CancellationToken ct)
    {
        var body = new Dictionary<string, string>
        {
            ["shopDomain"] = shopDomain,
            ["accessToken"] = accessToken
        };
        using var resp = await http.PostAsJsonAsync($"/internal/integrations/{merchantId}/shopify", body, ct);
        resp.EnsureSuccessStatusCode();
    }
}
