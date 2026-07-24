using Marketplace.BuildingBlocks.Domain;

namespace Marketplace.Catalog.Api.Domain;

/// <summary>Teklifin/kaydın hangi yolla kataloğa girdiği.</summary>
public static class ProductSource
{
    public const string Manual = "manual";
    public const string Excel = "excel";
    public const string Shopify = "shopify";

    public static readonly string[] All = [Manual, Excel, Shopify];
}

/// <summary>
/// PAZARYERİ ürün master'ı — tek fiziksel ürünü Barkod/GTIN ile tanımlar; pazaryerinde benzersizdir.
/// Mağazaya ait DEĞİLDİR: aynı master'ı N mağaza kendi <see cref="Offer"/>'ıyla farklı fiyattan satar
/// (Trendyol/Hepsiburada modeli). Fiyat/stok master'da değil, mağazanın teklifindedir.
/// Master'ı ilk ekleyen mağazanın verdiği bilgiler kartı oluşturur; sonraki satıcılar karta katılır.
/// </summary>
public class Product : AuditableEntity
{
    /// <summary>Ürünün evrensel kimliği (GTIN/EAN/UPC). Pazaryerinde benzersiz.</summary>
    public string Barcode { get; set; } = default!;

    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public string? Brand { get; set; }
    public Guid? CategoryId { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Kartı ilk oluşturan kaynak (manual/excel/shopify) — moderasyon/izlenebilirlik için.</summary>
    public string CreatedSource { get; set; } = ProductSource.Manual;

    public ICollection<Offer> Offers { get; set; } = new List<Offer>();
}

/// <summary>
/// PAZARYERİ kategori ağacı — tüm mağazalar ortak taksonomiyi kullanır (master'a bağlanır).
/// Taksonomiyi platform personeli yönetir; mağazalar yalnızca seçer.
/// </summary>
public class Category : AuditableEntity
{
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public Guid? ParentId { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Bir mağazanın belirli bir <see cref="Product"/> master'ını satma teklifi: kendi fiyatı, stoğu ve
/// (isteğe bağlı) kendi SKU'su. Mağaza başına master için TEK teklif (StoreId+ProductId benzersiz).
/// NOT: StockQuantity R7'de rezervasyonlu Inventory'ye devredilecek; şimdilik basit sayaçtır.
/// </summary>
public class Offer : AuditableStoreEntity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = default!;

    /// <summary>Mağazanın kendi ürün kodu (opsiyonel). Excel/Shopify eşlemesinde kullanılır.</summary>
    public string? Sku { get; set; }

    public decimal Price { get; set; }
    public decimal? CompareAtPrice { get; set; }
    public string Currency { get; set; } = "TRY";
    public int StockQuantity { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Teklifin kaynağı: manual / excel / shopify.</summary>
    public string Source { get; set; } = ProductSource.Manual;

    // --- Shopify besleyici alanları (R4): senkron eşlemesi ---
    public long? ShopifyProductId { get; set; }
    public long? ShopifyVariantId { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
}
