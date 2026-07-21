using System.Security.Claims;
using Marketplace.BuildingBlocks.MultiTenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Marketplace.BuildingBlocks.Web;

/// <summary>
/// JWT (Keycloak) claim'lerinden aktif merchant'ı çözer. Tüm servislerde ortak.
/// - "tenant_id" claim'i → merchant kimliği
/// - "platform-admin" / "owner" rolü → tüm tenant'ları görebilen platform kapsamı
/// </summary>
public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext context, ITenantContext tenant)
    {
        var user = context.User;
        var isPlatform = user.IsInRole("platform-admin") || user.IsInRole("owner");

        Guid? tenantId = null;
        if (Guid.TryParse(user.FindFirstValue("tenant_id"), out var parsed))
            tenantId = parsed;

        tenant.SetTenant(tenantId, isPlatform);
        await _next(context);
    }
}

public static class TenantMiddlewareExtensions
{
    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app)
        => app.UseMiddleware<TenantResolutionMiddleware>();
}
