namespace Marketplace.BuildingBlocks.Auditing;

/// <summary>
/// Denetim kaydı: hassas bir işlemi KİMİN, NE ZAMAN, HANGİ MAĞAZADA yaptığını saklar.
/// Yayınlama, kullanıcı yönetimi ve mağaza ayarları gibi geri alınması güç işlemler için tutulur.
/// İş verisiyle aynı SaveChanges içinde yazılır — işlem başarısız olursa kayıt da oluşmaz.
/// </summary>
public class AuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>İşlemin yapıldığı mağaza. Pazaryeri geneli işlemlerde (ör. CMS içeriği) null.</summary>
    public Guid? StoreId { get; set; }

    public string ActorId { get; set; } = default!;
    public string ActorName { get; set; } = default!;
    /// <summary>Aktörün o andaki rolleri (virgülle ayrık) — yetki geçmişini sonradan yorumlayabilmek için.</summary>
    public string? ActorRoles { get; set; }

    /// <summary>Platform personeli bir mağaza adına mı işlem yaptı (X-Acting-Store).</summary>
    public bool OnBehalfOfStore { get; set; }

    /// <summary>Nokta ile ayrılmış işlem adı: page.publish, user.role_changed, store.signup ...</summary>
    public string Action { get; set; } = default!;

    public string? EntityType { get; set; }
    public string? EntityId { get; set; }

    /// <summary>İnsan tarafından okunabilir özet (panelde listelenir).</summary>
    public string Summary { get; set; } = default!;

    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
