namespace Marketplace.BuildingBlocks.MultiTenancy;

/// <summary>
/// İstek başına çözülen tenant (merchant) kimliği. EF Core global query filter ve
/// yazma sırasında TenantId ataması bunu kullanır. Ortak DB + TenantId izolasyon modelinin çekirdeği.
/// </summary>
public interface ITenantContext
{
    /// <summary>Aktif merchant kimliği. Platform/owner kapsamı için null olabilir.</summary>
    Guid? TenantId { get; }

    /// <summary>Owner/platform yöneticisi kapsamında mı (tüm tenant'ları görebilir).</summary>
    bool IsPlatformScope { get; }

    void SetTenant(Guid? tenantId, bool isPlatformScope);
}

/// <summary>Scoped default implementasyon; değeri bir middleware JWT claim'inden doldurur.</summary>
public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }
    public bool IsPlatformScope { get; private set; }

    public void SetTenant(Guid? tenantId, bool isPlatformScope)
    {
        TenantId = tenantId;
        IsPlatformScope = isPlatformScope;
    }
}
