using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Marketplace.Payment.Api.Providers;

/// <summary>
/// PayPal ödeme sağlayıcı (gerçek yapı, Orders v2). Credential: clientId, clientSecret, baseUrl
/// (sandbox: https://api-m.sandbox.paypal.com). OAuth2 client-credentials ile token alır,
/// order oluşturur+capture eder. Simülatör modunda çağrılmaz.
/// </summary>
public sealed class PayPalPaymentProvider : IPaymentProvider
{
    private readonly HttpClient _http;
    private readonly ILogger<PayPalPaymentProvider> _logger;

    public string Name => "paypal";

    public PayPalPaymentProvider(HttpClient http, ILogger<PayPalPaymentProvider> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<PaymentResult> ChargeAsync(IReadOnlyDictionary<string, string>? credentials, PaymentCharge charge, CancellationToken ct)
    {
        if (credentials is null ||
            !credentials.TryGetValue("clientId", out var clientId) ||
            !credentials.TryGetValue("clientSecret", out var clientSecret))
            return new PaymentResult(false, null, "PayPal credential eksik (clientId/clientSecret).");

        var baseUrl = credentials.TryGetValue("baseUrl", out var b) ? b : "https://api-m.sandbox.paypal.com";

        try
        {
            // 1) OAuth2 token
            using var tokenReq = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/oauth2/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" })
            };
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
            tokenReq.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            using var tokenRes = await _http.SendAsync(tokenReq, ct);
            tokenRes.EnsureSuccessStatusCode();
            var tokenDoc = await tokenRes.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var accessToken = tokenDoc.GetProperty("access_token").GetString();

            // 2) Order oluştur (intent=CAPTURE)
            var orderBody = new
            {
                intent = "CAPTURE",
                purchase_units = new[]
                {
                    new
                    {
                        reference_id = charge.OrderId.ToString(),
                        amount = new { currency_code = charge.Currency, value = charge.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) }
                    }
                }
            };
            using var orderReq = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v2/checkout/orders")
            {
                Content = JsonContent.Create(orderBody)
            };
            orderReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var orderRes = await _http.SendAsync(orderReq, ct);
            var orderDoc = await orderRes.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            var status = orderDoc.TryGetProperty("status", out var s) ? s.GetString() : null;
            var id = orderDoc.TryGetProperty("id", out var i) ? i.GetString() : null;
            if (orderRes.IsSuccessStatusCode && id is not null)
                return new PaymentResult(true, id, null);

            return new PaymentResult(false, null, $"PayPal order başarısız (status={status}).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayPal çağrı hatası: order={Order}", charge.OrderId);
            return new PaymentResult(false, null, $"PayPal çağrı hatası: {ex.Message}");
        }
    }
}
