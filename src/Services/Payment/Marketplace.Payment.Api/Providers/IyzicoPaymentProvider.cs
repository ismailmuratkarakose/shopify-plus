using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Marketplace.Payment.Api.Providers;

/// <summary>
/// iyzico ödeme sağlayıcı (gerçek yapı). Credential: apiKey, secretKey, baseUrl
/// (sandbox: https://sandbox-api.iyzipay.com). Merchant config'inden çözülür.
///
/// NOT: iyzico auth imzası (IYZWSv2/HMAC) sürüme duyarlıdır; canlı sandbox ile
/// doğrulanmalıdır. Simülatör modunda bu implementasyon çağrılmaz.
/// </summary>
public sealed class IyzicoPaymentProvider : IPaymentProvider
{
    private readonly HttpClient _http;
    private readonly ILogger<IyzicoPaymentProvider> _logger;

    public string Name => "iyzico";

    public IyzicoPaymentProvider(HttpClient http, ILogger<IyzicoPaymentProvider> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<PaymentResult> ChargeAsync(IReadOnlyDictionary<string, string>? credentials, PaymentCharge charge, CancellationToken ct)
    {
        if (credentials is null ||
            !credentials.TryGetValue("apiKey", out var apiKey) ||
            !credentials.TryGetValue("secretKey", out var secretKey))
            return new PaymentResult(false, null, "iyzico credential eksik (apiKey/secretKey).");

        var baseUrl = credentials.TryGetValue("baseUrl", out var b) ? b : "https://sandbox-api.iyzipay.com";

        var body = new
        {
            locale = "tr",
            conversationId = charge.OrderId.ToString(),
            price = charge.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            paidPrice = charge.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            currency = charge.Currency
        };
        var json = JsonSerializer.Serialize(body);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/payment/auth")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        // IYZWS imzası (basitleştirilmiş; canlı ile doğrulanacak).
        var randomKey = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var hash = Convert.ToBase64String(SHA1.HashData(Encoding.UTF8.GetBytes(apiKey + randomKey + secretKey + json)));
        req.Headers.Add("Authorization", $"IYZWS {apiKey}:{hash}");
        req.Headers.Add("x-iyzi-rnd", randomKey);

        try
        {
            using var res = await _http.SendAsync(req, ct);
            var payload = await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var status = payload.TryGetProperty("status", out var s) ? s.GetString() : null;
            if (status == "success")
            {
                var pid = payload.TryGetProperty("paymentId", out var p) ? p.GetString() : Guid.NewGuid().ToString();
                return new PaymentResult(true, pid, null);
            }
            var msg = payload.TryGetProperty("errorMessage", out var e) ? e.GetString() : "iyzico reddetti.";
            return new PaymentResult(false, null, msg);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "iyzico çağrı hatası: order={Order}", charge.OrderId);
            return new PaymentResult(false, null, $"iyzico çağrı hatası: {ex.Message}");
        }
    }
}
