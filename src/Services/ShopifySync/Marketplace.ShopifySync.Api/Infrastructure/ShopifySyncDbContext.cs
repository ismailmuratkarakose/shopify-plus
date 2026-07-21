using Marketplace.BuildingBlocks.Domain;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Outbox;
using Marketplace.ShopifySync.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.ShopifySync.Api.Infrastructure;

public class ShopifySyncDbContext : DbContext
{
    private readonly ITenantContext _tenant;

    public ShopifySyncDbContext(DbContextOptions<ShopifySyncDbContext> options, ITenantContext tenant)
        : base(options)
        => _tenant = tenant;

    public DbSet<ShopifyIntegration> Integrations => Set<ShopifyIntegration>();
    public DbSet<ProductMapping> ProductMappings => Set<ProductMapping>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("shopify");

        modelBuilder.Entity<ShopifyIntegration>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ShopDomain).HasMaxLength(255).IsRequired();
            e.HasIndex(x => x.TenantId).IsUnique(); // merchant başına tek Shopify entegrasyonu
            e.HasQueryFilter(x => _tenant.IsPlatformScope || x.TenantId == _tenant.TenantId);
        });

        modelBuilder.Entity<ProductMapping>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Sku).HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.MarketplaceProductId }).IsUnique();
            e.HasQueryFilter(x => _tenant.IsPlatformScope || x.TenantId == _tenant.TenantId);
        });

        modelBuilder.AddOutboxMessage("shopify");
        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is ITenantOwned owned && entry.State == EntityState.Added &&
                owned.TenantId == Guid.Empty && _tenant.TenantId is { } tid)
                owned.TenantId = tid;

            if (entry.Entity is IAuditable audit && entry.State == EntityState.Modified)
                audit.UpdatedAt = DateTimeOffset.UtcNow;
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
