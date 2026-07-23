using System.Security.Claims;
using Marketplace.BuildingBlocks.MultiTenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Marketplace.BuildingBlocks.Web;

/// <summary>
/// JWT (Keycloak) claim'lerinden aktif mağazayı çözer. Tüm servislerde ortak.
///
/// Üç kullanıcı sınıfı:
/// - <b>Mağaza kullanıcıları</b> (store-admin ve alt rolleri): `store_id` claim'i kendi mağazalarını belirler.
/// - <b>Pazaryeri personeli</b> (owner / platform-admin / content-editor / publish-manager):
///   `store_id` claim'i YOKTUR; içerik ve platform verisi üzerinde pazaryeri kapsamında çalışırlar.
///   Belirli bir mağazada İŞLEM YAPMAK için <c>X-Acting-Store</c> başlığıyla o mağazanın kapsamına
///   girerler ("mağaza adına işlem"). Bu olmadan mağaza verisine yazma sahipsiz kayıt üretirdi.
/// - <b>Son müşteriler</b> pazaryerinin kendi kimliğiyle gelir (R5); mağaza kapsamları yoktur.
/// </summary>
public sealed class StoreResolutionMiddleware
{
    /// <summary>Mağaza kullanıcılarının JWT'sinde mağazayı belirten claim.</summary>
    public const string StoreClaim = "store_id";

    /// <summary>Platform personelinin adına işlem yaptığı mağazayı belirten istek başlığı.</summary>
    public const string ActingStoreHeader = "X-Acting-Store";

    private readonly RequestDelegate _next;

    public StoreResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext context, IStoreContext scope, ILogger<StoreResolutionMiddleware> logger)
    {
        var user = context.User;
        var isPlatform = user.IsInRole(Roles.PlatformAdmin) || user.IsInRole(Roles.Owner);
        // İçerik ekibi de pazaryeri personelidir: mağaza claim'i yoksa platform kapsamında okur
        // (ör. içerik doğrulaması tüm mağazaların ürünlerine bakabilir). Mağaza adına işlem
        // (aşağıda) yalnızca owner/platform-admin'e tanınır.
        var isContentStaff = user.IsInRole(Roles.PublishManager) || user.IsInRole(Roles.ContentEditor);

        Guid? storeId = null;
        if (Guid.TryParse(user.FindFirstValue(StoreClaim), out var parsed))
            storeId = parsed;

        // "Mağaza adına işlem": yalnızca platform personeli için geçerlidir. Mağaza kullanıcıları
        // bu başlığı gönderse bile yok sayılır — kendi mağaza claim'lerinin dışına çıkamazlar.
        if (isPlatform && context.Request.Headers.TryGetValue(ActingStoreHeader, out var headerValue))
        {
            if (Guid.TryParse(headerValue.ToString(), out var actingStore))
            {
                // Seçilen mağazanın kapsamına girilir: platform genelinde okuma bu istek için kapanır,
                // böylece yazılan kayıtlar doğru mağazaya ait olur ve görünen veri o mağazayla sınırlı kalır.
                scope.SetStore(actingStore, isPlatformScope: false);
                logger.LogInformation("Platform kullanıcısı {User}, {Store} mağazası adına işlem yapıyor.",
                    user.FindFirstValue("preferred_username") ?? user.FindFirstValue("sub"), actingStore);
                await _next(context);
                return;
            }

            logger.LogWarning("Geçersiz {Header} başlığı yok sayıldı: {Value}", ActingStoreHeader, headerValue.ToString());
        }

        scope.SetStore(storeId, storeId is null && (isPlatform || isContentStaff));
        await _next(context);
    }
}

public static class StoreMiddlewareExtensions
{
    public static IApplicationBuilder UseStoreResolution(this IApplicationBuilder app)
        => app.UseMiddleware<StoreResolutionMiddleware>();
}
