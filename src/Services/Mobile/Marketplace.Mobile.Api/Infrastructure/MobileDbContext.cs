using Marketplace.BuildingBlocks.Domain;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.Mobile.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Mobile.Api.Infrastructure;

public class MobileDbContext : DbContext
{
    private readonly IStoreContext _scope;

    public MobileDbContext(DbContextOptions<MobileDbContext> options, IStoreContext scope)
        : base(options)
        => _scope = scope;

    public DbSet<FavoriteProduct> Favorites => Set<FavoriteProduct>();
    public DbSet<RecentlyViewedProduct> RecentlyViewed => Set<RecentlyViewedProduct>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("mobile");

        modelBuilder.Entity<FavoriteProduct>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UserRef).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.StoreId, x.UserRef, x.ShopifyProductId }).IsUnique();
            e.HasQueryFilter(x => _scope.IsPlatformScope || x.StoreId == _scope.StoreId);
        });

        modelBuilder.Entity<RecentlyViewedProduct>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UserRef).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.StoreId, x.UserRef, x.ShopifyProductId }).IsUnique();
            e.HasIndex(x => new { x.StoreId, x.UserRef, x.ViewedAt });
            e.HasQueryFilter(x => _scope.IsPlatformScope || x.StoreId == _scope.StoreId);
        });

        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is IStoreOwned owned && entry.State == EntityState.Added &&
                owned.StoreId == Guid.Empty && _scope.StoreId is { } tid)
                owned.StoreId = tid;

            if (entry.Entity is IAuditable audit && entry.State == EntityState.Modified)
                audit.UpdatedAt = DateTimeOffset.UtcNow;
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
