using Marketplace.BuildingBlocks.Domain;

namespace Marketplace.Catalog.Api.Domain;

/// <summary>
/// GLOBAL ürün master'ı — pazaryerinde tek fiziksel ürünü Barkod/GTIN ile tanımlar.
/// Tenant'a AİT DEĞİLDİR: aynı master'ı birden çok merchant kendi <see cref="Offer"/>'ıyla satar.
/// Fiyat/stok master'da değil, merchant'ın Offer'ında/Inventory'sindedir.
/// </summary>
public class Product : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Ürünün evrensel kimliği (GTIN/EAN/UPC). Pazaryerinde benzersiz.</summary>
    public string Barcode { get; set; } = default!;

    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public string? Brand { get; set; }
    public Guid? CategoryId { get; set; }
    public string? ImageUrl { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    public ICollection<Offer> Offers { get; set; } = new List<Offer>();
}
