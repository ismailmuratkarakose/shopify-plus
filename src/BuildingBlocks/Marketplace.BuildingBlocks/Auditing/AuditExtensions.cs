using System.Security.Claims;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Marketplace.BuildingBlocks.Auditing;

/// <summary>
/// Denetim kaydı yazar. Aktör ve mağaza bilgisini istek bağlamından kendisi çıkarır;
/// çağıran yalnızca ne olduğunu bildirir.
/// </summary>
public interface IAuditLogger
{
    /// <summary>
    /// Kaydı bekleyen değişikliklere ekler — çağıran, iş verisiyle birlikte SaveChanges yapmalıdır.
    /// Böylece işlem geri alınırsa denetim kaydı da oluşmaz.
    /// </summary>
    void Record(string action, string summary, string? entityType = null, string? entityId = null);
}

public sealed class AuditLogger<TContext> : IAuditLogger where TContext : DbContext
{
    private readonly TContext _db;
    private readonly ITenantContext _tenant;
    private readonly IHttpContextAccessor _accessor;

    public AuditLogger(TContext db, ITenantContext tenant, IHttpContextAccessor accessor)
    {
        _db = db;
        _tenant = tenant;
        _accessor = accessor;
    }

    public void Record(string action, string summary, string? entityType = null, string? entityId = null)
    {
        var user = _accessor.HttpContext?.User;
        var roles = user?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray() ?? [];

        // Platform personeli bir mağaza adına mı çalışıyor? (X-Acting-Store başlığı)
        var onBehalf = _accessor.HttpContext?.Request.Headers
            .ContainsKey(TenantResolutionMiddleware.ActingStoreHeader) ?? false;

        _db.Set<AuditEntry>().Add(new AuditEntry
        {
            TenantId = _tenant.TenantId,
            ActorId = user?.FindFirstValue("sub") ?? "sistem",
            ActorName = user?.FindFirstValue("preferred_username") ?? user?.FindFirstValue("sub") ?? "sistem",
            ActorRoles = roles.Length > 0 ? string.Join(",", roles) : null,
            OnBehalfOfStore = onBehalf,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Summary = summary
        });
    }
}

public static class AuditExtensions
{
    /// <summary>
    /// AuditEntries tablosunu modele ekler. Servisin DbContext'inde OnModelCreating içinde çağrılır.
    /// </summary>
    /// <param name="tenantFilter">
    /// Kiracı filtresi. Çağıran, kendi DbContext ALANINI referans alan bir ifade vermelidir
    /// (ör. <c>x => _tenant.IsPlatformScope || x.TenantId == _tenant.TenantId</c>).
    /// DİKKAT: ITenantContext'i parametre olarak alıp closure'da yakalamak YANLIŞTIR — model bir kez
    /// kurulup önbelleğe alındığından ilk isteğin tenant nesnesi donar ve filtre sonraki isteklerde
    /// yanlış çalışır. DbContext alanı referans edildiğinde EF her sorguda yeniden değerlendirir.
    /// </param>
    public static void AddAuditLog(this ModelBuilder modelBuilder,
        System.Linq.Expressions.Expression<Func<AuditEntry, bool>> tenantFilter, string? schema = null)
    {
        modelBuilder.Entity<AuditEntry>(e =>
        {
            e.ToTable("AuditEntries", schema);
            e.HasQueryFilter(tenantFilter);
            e.HasKey(x => x.Id);
            e.Property(x => x.ActorId).HasMaxLength(200).IsRequired();
            e.Property(x => x.ActorName).HasMaxLength(200).IsRequired();
            e.Property(x => x.ActorRoles).HasMaxLength(500);
            e.Property(x => x.Action).HasMaxLength(100).IsRequired();
            e.Property(x => x.EntityType).HasMaxLength(100);
            e.Property(x => x.EntityId).HasMaxLength(100);
            e.Property(x => x.Summary).HasMaxLength(1000).IsRequired();
            // Mağaza bazlı, zamana göre azalan listeleme en sık sorgudur.
            e.HasIndex(x => new { x.TenantId, x.OccurredAt });
            e.HasIndex(x => x.Action);
        });
    }

    public static IServiceCollection AddAuditLogging<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddScoped<IAuditLogger, AuditLogger<TContext>>();
        return services;
    }
}
