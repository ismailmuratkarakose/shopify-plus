using Marketplace.BuildingBlocks.Domain;

namespace Marketplace.Cms.Api.Domain;

/// <summary>
/// Uzaktan yapılandırma bayrağı: mobil uygulamada bir özelliği uygulama güncellemesi
/// gerektirmeden açıp kapatmayı sağlar. Snapshot'a dahil edilir.
/// </summary>
public class FeatureFlag : AuditableTenantEntity
{
    public string Key { get; set; } = default!;
    public bool IsEnabled { get; set; }
    /// <summary>Bayrak yalnızca aç/kapa değilse taşıdığı değer (ör. varyant adı, limit).</summary>
    public string? Value { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// Yayınlanan içeriğin DEĞİŞMEZ, sürümlü anlık görüntüsü. Mobil uygulama tek çağrıda bunu okur;
/// içerik yayınlandıkça yeni sürüm üretilir (eski sürümler geçmişte kalır).
/// Bu sayede mobil taraf CMS'in iç modeline değil, sabit bir sözleşmeye bağlanır.
/// </summary>
public class ExperienceSnapshot : AuditableTenantEntity
{
    public int Version { get; set; }
    /// <summary>Tüm yayınlanan ekranlar + bayraklar (JSON).</summary>
    public string Json { get; set; } = "{}";
    public string? GeneratedBy { get; set; }
    /// <summary>Snapshot'ı doğuran olay: publish / flag_change / manual</summary>
    public string Reason { get; set; } = "publish";
}
