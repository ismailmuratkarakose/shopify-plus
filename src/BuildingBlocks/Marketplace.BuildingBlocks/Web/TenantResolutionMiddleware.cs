using System.Security.Claims;
using Marketplace.BuildingBlocks.MultiTenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Marketplace.BuildingBlocks.Web;

/// <summary>
/// JWT (Keycloak) claim'lerinden aktif mağazayı (tenant) çözer. Tüm servislerde ortak.
///
/// Üç kullanıcı sınıfı:
/// - <b>Mağaza kullanıcıları</b> (store-admin / publish-manager / content-editor): `tenant_id` claim'i
///   kendi mağazalarını belirler.
/// - <b>Platform personeli</b> (owner / platform-admin): `tenant_id` claim'i YOKTUR; varsayılan olarak
///   tüm mağazaları okuyabilen platform kapsamındadır. Belirli bir mağazada İŞLEM YAPMAK için
///   <c>X-Acting-Store</c> başlığıyla o mağazanın kapsamına girer ("mağaza adına işlem").
///   Bu olmadan yazma işlemleri sahipsiz kayıt üretirdi.
/// - <b>Son müşteriler</b> mobil tarafta Shopify kimliğiyle gelir; bu middleware'in konusu değildir.
/// </summary>
public sealed class TenantResolutionMiddleware
{
    /// <summary>Platform personelinin adına işlem yaptığı mağazayı belirten istek başlığı.</summary>
    public const string ActingStoreHeader = "X-Acting-Store";

    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext context, ITenantContext tenant, ILogger<TenantResolutionMiddleware> logger)
    {
        var user = context.User;
        var isPlatform = user.IsInRole(Roles.PlatformAdmin) || user.IsInRole(Roles.Owner);

        Guid? tenantId = null;
        if (Guid.TryParse(user.FindFirstValue("tenant_id"), out var parsed))
            tenantId = parsed;

        // "Mağaza adına işlem": yalnızca platform personeli için geçerlidir. Mağaza kullanıcıları
        // bu başlığı gönderse bile yok sayılır — kendi tenant claim'lerinin dışına çıkamazlar.
        if (isPlatform && context.Request.Headers.TryGetValue(ActingStoreHeader, out var headerValue))
        {
            if (Guid.TryParse(headerValue.ToString(), out var actingStore))
            {
                // Seçilen mağazanın kapsamına girilir: platform genelinde okuma bu istek için kapanır,
                // böylece yazılan kayıtlar doğru mağazaya ait olur ve görünen veri o mağazayla sınırlı kalır.
                tenant.SetTenant(actingStore, isPlatformScope: false);
                logger.LogInformation("Platform kullanıcısı {User}, {Store} mağazası adına işlem yapıyor.",
                    user.FindFirstValue("preferred_username") ?? user.FindFirstValue("sub"), actingStore);
                await _next(context);
                return;
            }

            logger.LogWarning("Geçersiz {Header} başlığı yok sayıldı: {Value}", ActingStoreHeader, headerValue.ToString());
        }

        tenant.SetTenant(tenantId, isPlatform);
        await _next(context);
    }
}

public static class TenantMiddlewareExtensions
{
    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app)
        => app.UseMiddleware<TenantResolutionMiddleware>();
}
