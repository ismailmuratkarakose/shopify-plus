using Marketplace.BuildingBlocks.Messaging;

namespace Marketplace.Contracts;

// Servisler arası TÜKETİLEN event'ler burada tek CLR tipinden tanımlanır.
// Yayınlayan ve tüketen aynı tipi kullanır → MassTransit MessageUrn eşleşmesi garanti.

/// <summary>Catalog bir ürün oluşturduğunda. Inventory stok kaydı açar, ShopifySync outbound push eder.</summary>
public record ProductCreatedIntegrationEvent : IntegrationEvent
{
    public Guid ProductId { get; init; }
    public string Sku { get; init; } = default!;
    public string Title { get; init; } = default!;
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public string Currency { get; init; } = default!;

    /// <summary>Ürünün kaynağı ("marketplace" / "shopify"). Döngü önleme: shopify kaynaklı ürün tekrar push edilmez.</summary>
    public string Source { get; init; } = "marketplace";
}

public record OrderLine
{
    public Guid ProductId { get; init; }
    public string Sku { get; init; } = default!;
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
}

/// <summary>Order bir sipariş oluşturduğunda. Inventory stok rezervasyonu için tüketir.</summary>
public record OrderPlacedIntegrationEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public IReadOnlyList<OrderLine> Lines { get; init; } = [];
    public decimal Total { get; init; }
    public string Currency { get; init; } = default!;
}

/// <summary>Inventory stok rezervasyonunu başarıyla yaptığında. Order siparişi onaylar.</summary>
public record StockReservedIntegrationEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
}

/// <summary>Inventory yeterli stok bulamadığında. Order siparişi reddeder.</summary>
public record StockReservationFailedIntegrationEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public string Reason { get; init; } = default!;
}

// --- Ödeme saga (Order ↔ Payment ↔ Inventory) ---

/// <summary>Stok rezerve edildikten sonra Order ödeme ister. Payment tüketir.</summary>
public record PaymentRequestedIntegrationEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = default!;
}

/// <summary>Ödeme başarılı. Order siparişi Paid yapar.</summary>
public record PaymentSucceededIntegrationEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public Guid PaymentId { get; init; }
    public string Provider { get; init; } = default!;
    public string ProviderPaymentId { get; init; } = default!;
}

/// <summary>Ödeme başarısız. Order siparişi PaymentFailed yapar + stok rezervini geri bırakır.</summary>
public record PaymentFailedIntegrationEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public string Reason { get; init; } = default!;
}

/// <summary>Ödeme başarısız olunca rezerve edilen stoğun geri bırakılması (telafi). Inventory tüketir.</summary>
public record StockReleaseRequestedIntegrationEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public IReadOnlyList<OrderLine> Lines { get; init; } = [];
}

// --- Shopify inbound (Shopify → Pazaryeri) ---

/// <summary>Shopify'dan ürün oluştu/güncellendi webhook'u. Catalog upsert eder (Source=shopify).</summary>
public record ProductUpsertedFromShopifyIntegrationEvent : IntegrationEvent
{
    public long ShopifyProductId { get; init; }
    public string Sku { get; init; } = default!;
    public string Title { get; init; } = default!;
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public string Currency { get; init; } = default!;
    public DateTimeOffset ShopifyUpdatedAt { get; init; }
}

/// <summary>Shopify'dan stok değişti webhook'u. Inventory ilgili SKU'nun stoğunu günceller.</summary>
public record StockChangedFromShopifyIntegrationEvent : IntegrationEvent
{
    public string Sku { get; init; } = default!;
    public int QuantityOnHand { get; init; }
}

// --- Merchant ---

public record MerchantRegisteredIntegrationEvent : IntegrationEvent
{
    public Guid MerchantId { get; init; }
    public string Name { get; init; } = default!;
    public string Slug { get; init; } = default!;
}

/// <summary>
/// Bir merchant Shopify/ödeme sağlayıcı bağladığında yayınlanır. Secret İÇERMEZ.
/// Tüketiciler (ör. ShopifySync) credential'ı Merchant internal endpoint'inden çeker.
/// </summary>
public record MerchantIntegrationConfiguredIntegrationEvent : IntegrationEvent
{
    public Guid MerchantId { get; init; }
    public string Provider { get; init; } = default!;
    public bool IsActive { get; init; }
}
