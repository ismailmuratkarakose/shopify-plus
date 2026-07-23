using Microsoft.Extensions.DependencyInjection;

namespace Marketplace.BuildingBlocks.Web;

/// <summary>Keycloak realm rolleri. Panel kullanıcıları bu rollerden birine sahiptir.</summary>
public static class Roles
{
    // --- Platform seviyesi ---
    public const string Owner = "owner";
    public const string PlatformAdmin = "platform-admin";

    // --- Pazaryeri içerik ekibi (platform personeli) ---
    /// <summary>Yayın yöneticisi: içeriği onaylar, yayınlar, yayından kaldırır, sürüm yönetir.</summary>
    public const string PublishManager = "publish-manager";
    /// <summary>İçerik editörü: içerik hazırlar/düzenler ama YAYINLAYAMAZ.</summary>
    public const string ContentEditor = "content-editor";

    // --- Mağaza seviyesi ---
    /// <summary>Mağaza yöneticisi: mağaza ayarları, kullanıcı yönetimi, ürün/sipariş/kargo işlemleri.</summary>
    public const string StoreAdmin = "store-admin";

    /// <summary>Eski rol adı; mağaza yöneticisiyle eşdeğer kabul edilir (geriye uyum).</summary>
    public const string Merchant = "merchant";
}

/// <summary>
/// Uç noktalara uygulanan izin adları. İçerik hazırlama ile yayınlama bilinçli olarak ayrılmıştır:
/// editör hazırlar, yayın yöneticisi canlıya alır.
/// </summary>
public static class Policies
{
    /// <summary>Platform yönetimi (mağaza açma vb.).</summary>
    public const string Owner = "owner";

    /// <summary>İçerik hazırlama/düzenleme (sayfa, bileşen, medya).</summary>
    public const string ContentEdit = "content.edit";

    /// <summary>Canlıya alma: yayınlama, geri alma, uzaktan yapılandırma değişikliği.</summary>
    public const string ContentPublish = "content.publish";

    /// <summary>Mağaza ayarları ve kullanıcı/rol yönetimi.</summary>
    public const string StoreManage = "store.manage";
}

public static class AuthorizationPolicyExtensions
{
    /// <summary>
    /// Tüm servislerde ortak izin matrisi (R1'de ayrıştı):
    /// - İçerik hattı PAZARYERİNİNDİR — mobil uygulama tektir, içeriğini pazaryerinin kendi
    ///   içerik ekibi yönetir: editör ⊂ yayın yöneticisi ⊂ platform. Mağaza rolleri DIŞINDADIR.
    /// - Mağaza hattı: mağaza yöneticisi kendi mağazasını yönetir; platform hepsini kapsar.
    /// </summary>
    public static IServiceCollection AddMarketplacePolicies(this IServiceCollection services)
    {
        var platform = new[] { Roles.Owner, Roles.PlatformAdmin };
        var storeAdmins = new[] { Roles.StoreAdmin, Roles.Merchant };

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Owner, p => p.RequireRole(platform))
            .AddPolicy(Policies.StoreManage, p => p.RequireRole([.. storeAdmins, .. platform]))
            .AddPolicy(Policies.ContentPublish, p => p.RequireRole([Roles.PublishManager, .. platform]))
            .AddPolicy(Policies.ContentEdit, p => p.RequireRole(
                [Roles.ContentEditor, Roles.PublishManager, .. platform]));

        return services;
    }
}
