using Marketplace.BuildingBlocks.Auditing;
using Marketplace.BuildingBlocks.Domain;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Outbox;
using Marketplace.Merchant.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Merchant.Api.Infrastructure;

public class MerchantDbContext : DbContext
{
    private readonly ITenantContext _tenant;

    public MerchantDbContext(DbContextOptions<MerchantDbContext> options, ITenantContext tenant)
        : base(options)
        => _tenant = tenant;

    public DbSet<Domain.Merchant> Merchants => Set<Domain.Merchant>();
    public DbSet<MerchantIntegration> Integrations => Set<MerchantIntegration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("merchant");

        modelBuilder.Entity<Domain.Merchant>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(220).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.CommissionRate).HasPrecision(5, 4);
            e.HasIndex(x => x.Slug).IsUnique();
            // Merchant = tenant. Owner tüm merchant'ları görür; merchant yalnızca kendini.
            e.HasQueryFilter(x => _tenant.IsPlatformScope || x.Id == _tenant.TenantId);
        });

        modelBuilder.Entity<MerchantIntegration>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Provider).HasMaxLength(20).IsRequired();
            e.Property(x => x.EncryptedConfig).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Provider }).IsUnique();
            e.HasQueryFilter(x => _tenant.IsPlatformScope || x.TenantId == _tenant.TenantId);
        });

        modelBuilder.AddOutboxMessage("merchant");

        modelBuilder.AddAuditLog(x => _tenant.IsPlatformScope || x.TenantId == _tenant.TenantId, "merchant");

        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyTenantAndAudit();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyTenantAndAudit()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is ITenantOwned owned &&
                entry.State == EntityState.Added &&
                owned.TenantId == Guid.Empty &&
                _tenant.TenantId is { } tid)
            {
                owned.TenantId = tid;
            }

            if (entry.Entity is IAuditable audit && entry.State == EntityState.Modified)
                audit.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }
}
