using System.Net;
using System.Text.Json;

namespace Marketplace.Payment.Api.Providers;

/// <summary>Merchant internal endpoint'inden bir merchant'ın ödeme sağlayıcı config'ini çeker.</summary>
public interface IMerchantCredentialClient
{
    Task<IReadOnlyDictionary<string, string>?> GetIntegrationConfigAsync(Guid merchantId, string provider, CancellationToken ct);
}

public sealed class MerchantCredentialClient : IMerchantCredentialClient
{
    private readonly HttpClient _http;
    public MerchantCredentialClient(HttpClient http) => _http = http;

    public async Task<IReadOnlyDictionary<string, string>?> GetIntegrationConfigAsync(Guid merchantId, string provider, CancellationToken ct)
    {
        using var response = await _http.GetAsync($"/internal/integrations/{merchantId}/{provider}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("config", out var cfg))
            return null;

        var dict = new Dictionary<string, string>();
        foreach (var p in cfg.EnumerateObject())
            dict[p.Name] = p.Value.GetString() ?? "";
        return dict;
    }
}
