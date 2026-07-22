namespace Marketplace.Bff.Mobile.Api.Clients;

// Upstream servislerin döndürdüğü sözleşmelerin BFF tarafındaki karşılıkları.
// (Servis projelerine referans vermeden, sadece taşınan alanlar.)

public record CatalogProduct(
    Guid Id,
    string Sku,
    string Title,
    string? Description,
    decimal Price,
    string Currency,
    bool IsActive);

public record InventoryItem(
    Guid ProductId,
    string Sku,
    int QuantityOnHand,
    int QuantityReserved,
    int Available);

// Order servisi POST /api/orders gövdesi.
public record CreateOrderItemUpstream(Guid ProductId, string Sku, int Quantity, decimal UnitPrice);
public record CreateOrderUpstream(string Currency, IReadOnlyList<CreateOrderItemUpstream> Items);

// Order servisi yanıtı.
public record UpstreamOrderItem(Guid ProductId, string Sku, int Quantity, decimal UnitPrice, decimal LineTotal);
public record UpstreamOrder(
    Guid Id,
    string Status,
    string Currency,
    decimal TotalAmount,
    string? StatusReason,
    IReadOnlyList<UpstreamOrderItem> Items);
