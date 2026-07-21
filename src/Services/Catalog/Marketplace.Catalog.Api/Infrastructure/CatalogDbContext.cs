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
    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("catalog");

        modelBuilder.Entity<Category>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(220).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Slug }).IsUnique();
            // Merchant izolasyonu: her sorgu otomatik olarak aktif tenant'a filtrelenir.
            e.HasQueryFilter(x => _tenant.IsPlatformScope || x.TenantId == _tenant.TenantId);
        });

        modelBuilder.Entity<Product>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Sku).HasMaxLength(100).IsRequired();
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.Property(x => x.Source).HasMaxLength(20).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Sku }).IsUnique();
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
