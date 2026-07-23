namespace Marketplace.BuildingBlocks.MultiTenancy;

/// <summary>
/// İstek başına çözülen MAĞAZA kapsamı.
///
/// Kavram netliği (R1, 2026-07-23): platform TEK pazaryeridir; "tenant" sözcüğü artık pazaryerini
/// ifade eder ve tek pazaryeri kurulumunda örtüktür (ayrı bir kimlik taşınmaz — ileride SaaS
/// gerekirse ayrı bir MarketplaceId boyutu eklenir). Mağaza (store) ise pazaryerinin ALT varlığıdır;
/// veri izolasyonunun konusu budur. EF Core global query filter'ları ve yazma sırasında StoreId
/// ataması bu bağlamı kullanır.
/// </summary>
public interface IStoreContext
{
    /// <summary>Aktif mağaza kimliği. Platform personeli (mağaza seçmemiş) için null olabilir.</summary>
    Guid? StoreId { get; }

    /// <summary>Pazaryeri personeli kapsamında mı (tüm mağazaları görebilir).</summary>
    bool IsPlatformScope { get; }

    void SetStore(Guid? storeId, bool isPlatformScope);
}

/// <summary>Scoped default implementasyon; değeri bir middleware JWT claim'inden doldurur.</summary>
public sealed class StoreContext : IStoreContext
{
    public Guid? StoreId { get; private set; }
    public bool IsPlatformScope { get; private set; }

    public void SetStore(Guid? storeId, bool isPlatformScope)
    {
        StoreId = storeId;
        IsPlatformScope = isPlatformScope;
    }
}
