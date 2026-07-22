using Marketplace.BuildingBlocks.Domain;

namespace Marketplace.Catalog.Api.Domain;

/// <summary>
/// GLOBAL ürün taksonomisi — tüm merchant'lar ortak kategori ağacını kullanır (master'a bağlanır).
/// Tenant'a ait değildir.
/// </summary>
public class Category : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public Guid? ParentId { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
