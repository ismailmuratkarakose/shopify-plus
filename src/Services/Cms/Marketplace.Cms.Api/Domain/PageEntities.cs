using Marketplace.BuildingBlocks.Domain;

namespace Marketplace.Cms.Api.Domain;

/// <summary>Mobil uygulamada yönetilebilen ekran tipleri.</summary>
public enum ScreenType
{
    Home = 0,           // Ana sayfa
    ProductList = 1,    // Ürün listeleme
    ProductDetail = 2,  // Ürün detay
    Cart = 3,           // Sepet
    Campaign = 4,       // Kampanya sayfası
    Landing = 5         // Landing page
}

public static class ScreenTypes
{
    /// <summary>
    /// Uygulamada TEK bir örneği olabilen ekranlar. Bunlar için birden fazla sayfa tanımlanamaz;
    /// aksi hâlde mobil tarafta hangi sayfanın gösterileceği belirsiz kalır.
    /// Kampanya ve landing sayfaları çoklu olabilir (handle ile ayrışır).
    /// </summary>
    public static bool IsSingleton(ScreenType type) => type is
        ScreenType.Home or ScreenType.ProductList or ScreenType.ProductDetail or ScreenType.Cart;
}

/// <summary>İçerik yaşam döngüsü durumu.</summary>
public enum VersionStatus
{
    Draft = 0,      // Taslak — düzenlenebilir, canlıyı etkilemez
    Published = 1,  // Yayında — mobil uygulamanın gördüğü sürüm
    Archived = 2    // Arşiv — daha önce yayınlanmış, geçmişte kalan sürüm
}

/// <summary>
/// Mantıksal sayfa (ekran). İçerik doğrudan burada değil, sürümlerde (<see cref="PageVersion"/>) tutulur:
/// böylece taslak düzenlenirken yayındaki içerik etkilenmez.
/// </summary>
public class Page : AuditableEntity
{
    public ScreenType ScreenType { get; set; }
    public string Name { get; set; } = default!;
    /// <summary>Pazaryeri içinde benzersiz kısa ad (ör. "ana-sayfa", "yaz-kampanyasi").</summary>
    public string Handle { get; set; } = default!;
    public bool IsActive { get; set; } = true;

    /// <summary>Yayındaki sürüm (yoksa sayfa henüz hiç yayınlanmamıştır).</summary>
    public Guid? PublishedVersionId { get; set; }

    public List<PageVersion> Versions { get; set; } = [];
}

/// <summary>Bir sayfanın belirli bir içerik sürümü. Yayınlanan sürüm dondurulur; düzenleme yeni taslakta sürer.</summary>
public class PageVersion : AuditableEntity
{
    public Guid PageId { get; set; }
    public int VersionNumber { get; set; }
    public VersionStatus Status { get; set; } = VersionStatus.Draft;

    public DateTimeOffset? PublishedAt { get; set; }
    public string? PublishedBy { get; set; }
    /// <summary>Sürüm notu (ör. "yaz kampanyası bannerı eklendi").</summary>
    public string? Note { get; set; }

    public List<PageComponent> Components { get; set; } = [];
}

/// <summary>
/// Sürüm içindeki tek bir bileşen. Tip bazlı ayarlar <see cref="SettingsJson"/> içinde JSON olarak tutulur;
/// eklenirken/güncellenirken tip kayıt defterine göre doğrulanır.
/// </summary>
public class PageComponent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PageVersionId { get; set; }

    /// <summary>Bileşen tipi (bkz. ComponentTypes): banner, product_grid, collection, campaign, popup, personalization, dynamic_content.</summary>
    public string Type { get; set; } = default!;

    /// <summary>Ekrandaki sıra (0'dan başlar). Sürükle-bırak sıralaması bunu günceller.</summary>
    public int Position { get; set; }

    public string SettingsJson { get; set; } = "{}";
    public bool IsActive { get; set; } = true;
}
