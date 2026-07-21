using Marketplace.BuildingBlocks.Domain;

namespace Marketplace.Merchant.Api.Domain;

public static class IntegrationProvider
{
    public const string Shopify = "shopify";
    public const string Iyzico = "iyzico";
    public const string PayPal = "paypal";

    public static readonly string[] All = [Shopify, Iyzico, PayPal];
}

/// <summary>
/// Bir merchant'ın Shopify / ödeme sağlayıcı entegrasyon konfigürasyonu.
/// Hassas anahtarlar <see cref="EncryptedConfig"/> içinde AES-GCM ile şifreli tutulur.
/// TenantId, bu entegrasyonun ait olduğu merchant'tır.
/// </summary>
public class MerchantIntegration : AuditableTenantEntity
{
    public string Provider { get; set; } = default!;

    /// <summary>Şifreli (at-rest) konfigürasyon JSON'ı. Asla düz metin dönülmez.</summary>
    public string EncryptedConfig { get; set; } = default!;

    public bool IsActive { get; set; } = true;
}
