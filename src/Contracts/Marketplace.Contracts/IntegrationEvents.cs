using Marketplace.BuildingBlocks.Messaging;

namespace Marketplace.Contracts;

// Servisler arası TÜKETİLEN event'ler burada tek CLR tipinden tanımlanır.
// Yayınlayan ve tüketen aynı tipi kullanır → MassTransit MessageUrn eşleşmesi garanti.
//
// NOT: Shopify Deneyim Platformu'na geçişte pazaryeri event'leri (ürün/sipariş/ödeme saga,
// Shopify-inbound katalog event'leri) kaldırıldı — Shopify kaynak sistemdir ve senkron
// ShopifySync içinde read-model'e yapılır. Kalan event'ler mağaza (Store) yaşam döngüsüdür.

// --- Store (Merchant) ---

public record MerchantRegisteredIntegrationEvent : IntegrationEvent
{
    public Guid MerchantId { get; init; }
    public string Name { get; init; } = default!;
    public string Slug { get; init; } = default!;

    /// <summary>(Dormant) pazaryeri döneminden kalan komisyon oranı; Store modelinde kullanılmıyor.</summary>
    public decimal CommissionRate { get; init; }
}

/// <summary>
/// Bir mağaza Shopify entegrasyonu bağladığında yayınlanır. Secret İÇERMEZ.
/// ShopifySync bunu tüketip credential'ı Merchant internal endpoint'inden çeker (read-model).
/// </summary>
public record MerchantIntegrationConfiguredIntegrationEvent : IntegrationEvent
{
    public Guid MerchantId { get; init; }
    public string Provider { get; init; } = default!;
    public bool IsActive { get; init; }
}
