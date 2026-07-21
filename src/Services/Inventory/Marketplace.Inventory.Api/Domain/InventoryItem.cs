using Marketplace.BuildingBlocks.Domain;

namespace Marketplace.Inventory.Api.Domain;

/// <summary>Bir merchant'ın bir ürünü için stok kaydı.</summary>
public class InventoryItem : AuditableTenantEntity
{
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = default!;
    public int QuantityOnHand { get; set; }
    public int QuantityReserved { get; set; }

    public int Available => QuantityOnHand - QuantityReserved;
}
