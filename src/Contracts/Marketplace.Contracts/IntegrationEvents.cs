using Marketplace.BuildingBlocks.Messaging;

namespace Marketplace.Contracts;

// Servisler arası TÜKETİLEN event'ler burada tek CLR tipinden tanımlanır.
// Yayınlayan ve tüketen aynı tipi kullanır → MassTransit MessageUrn eşleşmesi garanti.

/// <summary>Catalog bir ürün oluşturduğunda. Inventory bunu tüketip stok kaydı açar.</summary>
public record ProductCreatedIntegrationEvent : IntegrationEvent
{
    public Guid ProductId { get; init; }
    public string Sku { get; init; } = default!;
    public string Title { get; init; } = default!;
    public decimal Price { get; init; }
    public string Currency { get; init; } = default!;
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
