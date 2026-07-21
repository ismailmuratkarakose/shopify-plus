using Marketplace.BuildingBlocks.Domain;

namespace Marketplace.Order.Api.Domain;

public enum OrderStatus
{
    Pending = 0,     // oluşturuldu, stok rezervasyonu bekleniyor
    Confirmed = 1,   // stok rezerve edildi
    Rejected = 2     // yetersiz stok
}

public class Order : AuditableTenantEntity
{
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public string Currency { get; set; } = "TRY";
    public decimal TotalAmount { get; set; }
    public string? StatusReason { get; set; }

    public List<OrderItem> Items { get; set; } = [];
}

public class OrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = default!;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal => Quantity * UnitPrice;
}
