using Marketplace.BuildingBlocks.Domain;

namespace Marketplace.Cms.Api.Domain;

/// <summary>
/// Yayınlanmamış taslağı yetkisiz bir cihazda (ör. test telefonu) görüntülemek için üretilen süreli anahtar.
/// Anahtar hem sayfayı hem mağazayı (tenant) belirler; böylece önizleme ucu anonim çalışabilir.
/// </summary>
public class PreviewToken : AuditableTenantEntity
{
    public Guid PageId { get; set; }
    public string Token { get; set; } = default!;
    public DateTimeOffset ExpiresAt { get; set; }
    public string? CreatedBy { get; set; }
    public bool IsRevoked { get; set; }

    public bool IsValid(DateTimeOffset now) => !IsRevoked && ExpiresAt > now;
}

/// <summary>Yüklenen görsel/medya varlığı. Dosyanın kendisi <see cref="StoragePath"/> altında saklanır.</summary>
public class MediaAsset : AuditableTenantEntity
{
    public string FileName { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public long SizeBytes { get; set; }
    /// <summary>Depolama sağlayıcısına özel yol/anahtar (yerel dosya adı veya nesne anahtarı).</summary>
    public string StoragePath { get; set; } = default!;
    public string? UploadedBy { get; set; }
}
