using Marketplace.BuildingBlocks.Domain;

namespace Marketplace.Catalog.Api.Domain;

public class Category : AuditableTenantEntity
{
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public Guid? ParentId { get; set; }
    public bool IsActive { get; set; } = true;
}
