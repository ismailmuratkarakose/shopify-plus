using Marketplace.BuildingBlocks.Domain;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Outbox;
using Marketplace.Catalog.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Catalog.Api.Infrastructure;

public class CatalogDbContext : DbContext
{
    private readonly ITenantContext _tenant;

    public CatalogDbContext(DbContextOptions<CatalogDbContext> options, ITenantContext tenant)
        : base(options)
        => _tenant = tenant;

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("catalog");

        // Kategori: GLOBAL taksonomi (tenant filtresi yok).
        modelBuilder.Entity<Category>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(220).IsRequired();
            e.HasIndex(x => x.Slug).IsUnique();
        });

        // Ürün master: GLOBAL, Barkod ile benzersiz (tenant filtresi yok).
        modelBuilder.Entity<Product>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Barcode).HasMaxLength(64).IsRequired();
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Brand).HasMaxLength(200);
            e.HasIndex(x => x.Barcode).IsUnique();
            e.HasMany(x => x.Offers).WithOne(o => o.Product).HasForeignKey(o => o.ProductId);
        });

        // Teklif: tenant'a ait; merchant başına master için tek offer.
        modelBuilder.Entity<Offer>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Sku).HasMaxLength(100);
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.Property(x => x.Source).HasMaxLength(20).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.ProductId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.ShopifyProductId });
            e.HasQueryFilter(x => _tenant.IsPlatformScope || x.TenantId == _tenant.TenantId);
        });

        modelBuilder.AddOutboxMessage("catalog");

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
