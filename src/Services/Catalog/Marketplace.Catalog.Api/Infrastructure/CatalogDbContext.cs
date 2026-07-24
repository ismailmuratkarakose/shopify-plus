using Marketplace.BuildingBlocks.Auditing;
using Marketplace.BuildingBlocks.Domain;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.Catalog.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Catalog.Api.Infrastructure;

/// <summary>
/// Katalog iki katmandan oluşur (R2):
/// - PAZARYERİ katmanı (mağaza filtresi YOK): ürün master'ı (barkod) + kategori ağacı.
/// - MAĞAZA katmanı (StoreId filtreli): satıcı teklifleri (fiyat/stok/SKU).
/// Kamusal okuma uçları teklifleri IgnoreQueryFilters ile mağazalar arası birleştirir.
/// </summary>
public class CatalogDbContext : DbContext
{
    private readonly IStoreContext _scope;

    public CatalogDbContext(DbContextOptions<CatalogDbContext> options, IStoreContext scope)
        : base(options)
        => _scope = scope;

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("catalog");

        // Kategori: pazaryeri taksonomisi (mağaza filtresi yok).
        modelBuilder.Entity<Category>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(220).IsRequired();
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.ParentId);
        });

        // Ürün master: pazaryeri geneli, Barkod ile benzersiz (mağaza filtresi yok).
        modelBuilder.Entity<Product>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Barcode).HasMaxLength(64).IsRequired();
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Brand).HasMaxLength(200);
            e.Property(x => x.ImageUrl).HasMaxLength(1000);
            e.Property(x => x.CreatedSource).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Barcode).IsUnique();
            e.HasIndex(x => x.CategoryId);
            e.HasMany(x => x.Offers).WithOne(o => o.Product).HasForeignKey(o => o.ProductId);
        });

        // Teklif: mağazaya ait; mağaza başına master için tek teklif.
        modelBuilder.Entity<Offer>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Sku).HasMaxLength(100);
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.Property(x => x.CompareAtPrice).HasPrecision(18, 2);
            e.Property(x => x.Source).HasMaxLength(20).IsRequired();
            e.HasIndex(x => new { x.StoreId, x.ProductId }).IsUnique();
            e.HasIndex(x => new { x.StoreId, x.ShopifyVariantId });
            e.HasQueryFilter(x => _scope.IsPlatformScope || x.StoreId == _scope.StoreId);
        });

        modelBuilder.AddAuditLog(x => _scope.IsPlatformScope || x.StoreId == _scope.StoreId, "catalog");

        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is IStoreOwned owned && entry.State == EntityState.Added &&
                owned.StoreId == Guid.Empty && _scope.StoreId is { } sid)
                owned.StoreId = sid;

            if (entry.Entity is IAuditable audit && entry.State == EntityState.Modified)
                audit.UpdatedAt = DateTimeOffset.UtcNow;
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
