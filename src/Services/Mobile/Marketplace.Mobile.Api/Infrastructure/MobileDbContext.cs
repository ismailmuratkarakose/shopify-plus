using Marketplace.BuildingBlocks.Domain;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.Mobile.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Mobile.Api.Infrastructure;

public class MobileDbContext : DbContext
{
    private readonly ITenantContext _tenant;

    public MobileDbContext(DbContextOptions<MobileDbContext> options, ITenantContext tenant)
        : base(options)
        => _tenant = tenant;

    public DbSet<FavoriteProduct> Favorites => Set<FavoriteProduct>();
    public DbSet<RecentlyViewedProduct> RecentlyViewed => Set<RecentlyViewedProduct>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("mobile");

        modelBuilder.Entity<FavoriteProduct>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UserRef).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.UserRef, x.ShopifyProductId }).IsUnique();
            e.HasQueryFilter(x => _tenant.IsPlatformScope || x.TenantId == _tenant.TenantId);
        });

        modelBuilder.Entity<RecentlyViewedProduct>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UserRef).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.UserRef, x.ShopifyProductId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.UserRef, x.ViewedAt });
            e.HasQueryFilter(x => _tenant.IsPlatformScope || x.TenantId == _tenant.TenantId);
        });

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
