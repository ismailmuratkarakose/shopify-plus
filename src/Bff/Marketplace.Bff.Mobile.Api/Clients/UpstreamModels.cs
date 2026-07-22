namespace Marketplace.Bff.Mobile.Api.Clients;

// Upstream Catalog (master + offer) sözleşmeleri.
public record ProductListItem(Guid Id, string Barcode, string Title, string? Brand, int OfferCount, decimal? MinPrice);
public record ProductMaster(Guid Id, string Barcode, string Title, string? Description, string? Brand, Guid? CategoryId, string? ImageUrl);
public record SellerOffer(Guid OfferId, Guid MerchantId, string? Sku, decimal Price, string Currency, bool IsActive);
public record ProductWithOffers(ProductMaster Product, IReadOnlyList<SellerOffer> Offers);

// Order servisi POST /api/orders gövdesi — MerchantId = satıcı (checkout böler).
public record CreateOrderItemUpstream(Guid ProductId, string Sku, int Quantity, decimal UnitPrice);
public record CreateOrderUpstream(string Currency, IReadOnlyList<CreateOrderItemUpstream> Items, Guid MerchantId);

// Order servisi yanıtı.
public record UpstreamOrderItem(Guid ProductId, string Sku, int Quantity, decimal UnitPrice, decimal LineTotal);
public record UpstreamOrder(
    Guid Id,
    Guid MerchantId,
    string? BuyerRef,
    string Status,
    string Currency,
    decimal TotalAmount,
    string? StatusReason,
    IReadOnlyList<UpstreamOrderItem> Items);
