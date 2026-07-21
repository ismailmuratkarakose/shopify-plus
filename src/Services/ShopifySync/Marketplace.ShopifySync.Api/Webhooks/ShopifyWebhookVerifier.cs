using System.Security.Cryptography;
using System.Text;

namespace Marketplace.ShopifySync.Api.Webhooks;

/// <summary>
/// Shopify webhook imza doğrulaması: HMAC-SHA256(raw body, app secret) → base64,
/// X-Shopify-Hmac-Sha256 header'ıyla sabit-zamanlı karşılaştırılır.
/// </summary>
public static class ShopifyWebhookVerifier
{
    public static bool IsValid(byte[] rawBody, string? hmacHeader, string secret)
    {
        if (string.IsNullOrEmpty(hmacHeader) || string.IsNullOrEmpty(secret))
            return false;

        var computed = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), rawBody);
        var computedB64 = Convert.ToBase64String(computed);

        var a = Encoding.UTF8.GetBytes(computedB64);
        var b = Encoding.UTF8.GetBytes(hmacHeader);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
