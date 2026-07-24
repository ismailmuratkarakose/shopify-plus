using Marketplace.BuildingBlocks.Domain;
using Marketplace.Mobile.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Mobile.Api.Infrastructure;

/// <summary>
/// Mobil kullanıcı listeleri (favoriler, son gezilenler) — R4'ten itibaren ortak katalog
/// kartlarına işaret eder ve pazaryeri genelindedir (mağaza filtresi yok).
/// </summary>
public class MobileDbContext : DbContext
{
    public MobileDbContext(DbContextOptions<MobileDbContext> options) : base(options) { }

    public DbSet<FavoriteProduct> Favorites => Set<FavoriteProduct>();
    public DbSet<RecentlyViewedProduct> RecentlyViewed => Set<RecentlyViewedProduct>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("mobile");

        modelBuilder.Entity<FavoriteProduct>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UserRef).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.UserRef, x.ProductId }).IsUnique();
        });

        modelBuilder.Entity<RecentlyViewedProduct>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UserRef).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.UserRef, x.ProductId }).IsUnique();
            e.HasIndex(x => new { x.UserRef, x.ViewedAt });
        });

        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is IAuditable audit && entry.State == EntityState.Modified)
                audit.UpdatedAt = DateTimeOffset.UtcNow;
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
