using Marketplace.Reporting.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Reporting.Api.Infrastructure;

/// <summary>
/// Raporlama read-model'i. Diğer servislerin aksine burada global tenant query filter YOKTUR:
/// owner (platform) kapsamı tüm tenant'ları görür, merchant kapsamı sorgularda manuel filtrelenir.
/// Servis event tüketir, event yayınlamaz → outbox yoktur.
/// </summary>
public class ReportingDbContext : DbContext
{
    public ReportingDbContext(DbContextOptions<ReportingDbContext> options) : base(options) { }

    public DbSet<MerchantRate> MerchantRates => Set<MerchantRate>();
    public DbSet<SalesFact> Sales => Set<SalesFact>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("reporting");

        modelBuilder.Entity<MerchantRate>(e =>
        {
            e.HasKey(x => x.TenantId);
            e.Property(x => x.Name).HasMaxLength(300).IsRequired();
            e.Property(x => x.CommissionRate).HasPrecision(5, 4);
        });

        modelBuilder.Entity<SalesFact>(e =>
        {
            e.HasKey(x => x.OrderId);
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.CommissionRate).HasPrecision(5, 4);
            e.Property(x => x.CommissionAmount).HasPrecision(18, 2);
            e.Property(x => x.NetAmount).HasPrecision(18, 2);
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.HasIndex(x => x.PaidAt);
        });

        base.OnModelCreating(modelBuilder);
    }
}
