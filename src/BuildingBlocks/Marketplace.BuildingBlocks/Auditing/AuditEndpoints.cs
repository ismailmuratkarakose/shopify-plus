using Marketplace.BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.BuildingBlocks.Auditing;

public record AuditEntryDto(Guid Id, Guid? StoreId, string ActorName, string? ActorRoles,
    bool OnBehalfOfStore, string Action, string? EntityType, string? EntityId,
    string Summary, DateTimeOffset OccurredAt);

public static class AuditEndpoints
{
    /// <summary>
    /// Denetim kaydı sorgulama ucu (mağaza kapsamına göre filtrelenir).
    /// Yalnızca mağaza/platform yöneticileri erişebilir.
    /// </summary>
    /// <param name="path">
    /// Uç yolu. Birden fazla servis denetim kaydı sunduğu için gateway'de ayrışabilmeleri gerekir
    /// (ör. içerik denetimi /api/audit, hesap denetimi /api/audit/account).
    /// </param>
    /// <param name="policy">
    /// Erişim politikası. İçerik denetimi pazaryeri personelinindir (content.publish);
    /// hesap denetimi mağaza yöneticilerinindir (store.manage — varsayılan).
    /// </param>
    public static IEndpointRouteBuilder MapAuditEndpoints<TContext>(
        this IEndpointRouteBuilder app, string path = "/api/audit", string policy = Policies.StoreManage)
        where TContext : DbContext
    {
        app.MapGet(path, async (TContext db, CancellationToken ct,
            string? action = null, int page = 1, int pageSize = 50) =>
        {
            var p = Math.Max(1, page);
            var size = Math.Clamp(pageSize, 1, 200);

            // Kapsam, model üzerindeki mağaza query filter'ı ile uygulanır (mağaza kendi kayıtlarını görür).
            var q = db.Set<AuditEntry>().AsQueryable();
            if (!string.IsNullOrWhiteSpace(action))
                q = q.Where(a => a.Action == action);

            var total = await q.CountAsync(ct);
            var items = await q.OrderByDescending(a => a.OccurredAt)
                .Skip((p - 1) * size).Take(size)
                .Select(a => new AuditEntryDto(a.Id, a.StoreId, a.ActorName, a.ActorRoles,
                    a.OnBehalfOfStore, a.Action, a.EntityType, a.EntityId, a.Summary, a.OccurredAt))
                .ToListAsync(ct);

            // Tam nitelendirilmiş: bu assembly'de Marketplace.BuildingBlocks.Results namespace'i de var.
            return Microsoft.AspNetCore.Http.Results.Ok(new { total, page = p, pageSize = size, items });
        })
        .RequireAuthorization(policy)
        .WithTags("Audit");

        return app;
    }
}
