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
    public DbSet<WebhookInbox> WebhookInbox => Set<WebhookInbox>();
    public DbSet<SyncedProduct> SyncedProducts => Set<SyncedProduct>();
    public DbSet<SyncedCollection> SyncedCollections => Set<SyncedCollection>();
    public DbSet<SyncedOrder> SyncedOrders => Set<SyncedOrder>();
    public DbSet<SyncedCustomer> SyncedCustomers => Set<SyncedCustomer>();

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
            e.HasIndex(x => new { x.TenantId, x.Sku }).IsUnique();
            e.HasQueryFilter(x => _tenant.IsPlatformScope || x.TenantId == _tenant.TenantId);
        });

        modelBuilder.Entity<WebhookInbox>(e =>
        {
            e.HasKey(x => x.WebhookId);
            e.Property(x => x.WebhookId).HasMaxLength(200);
            e.Property(x => x.Topic).HasMaxLength(100);
        });

        // --- Shopify read-model'leri (kaynak Shopify; tenant = mağaza) ---
        modelBuilder.Entity<SyncedProduct>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(500).IsRequired();
            e.Property(x => x.Handle).HasMaxLength(300).IsRequired();
            e.Property(x => x.Status).HasMaxLength(30).IsRequired();
            e.Property(x => x.Vendor).HasMaxLength(200);
            e.Property(x => x.ProductType).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.ShopifyProductId }).IsUnique();
            e.HasMany(x => x.Variants).WithOne().HasForeignKey(v => v.SyncedProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenant.IsPlatformScope || x.TenantId == _tenant.TenantId);
        });

        modelBuilder.Entity<SyncedVariant>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Sku).HasMaxLength(100);
            e.Property(x => x.Barcode).HasMaxLength(64);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.Property(x => x.CompareAtPrice).HasPrecision(18, 2);
        });

        modelBuilder.Entity<SyncedCollection>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(500).IsRequired();
            e.Property(x => x.Handle).HasMaxLength(300).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.ShopifyCollectionId }).IsUnique();
            e.HasMany(x => x.Products).WithOne().HasForeignKey(p => p.SyncedCollectionId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenant.IsPlatformScope || x.TenantId == _tenant.TenantId);
        });

        modelBuilder.Entity<SyncedCollectionProduct>(e => e.HasKey(x => x.Id));

        modelBuilder.Entity<SyncedOrder>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(50).IsRequired();
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.FinancialStatus).HasMaxLength(30).IsRequired();
            e.Property(x => x.FulfillmentStatus).HasMaxLength(30);
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.TotalPrice).HasPrecision(18, 2);
            e.HasIndex(x => new { x.TenantId, x.ShopifyOrderId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.ShopifyCustomerId });
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.SyncedOrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenant.IsPlatformScope || x.TenantId == _tenant.TenantId);
        });

        modelBuilder.Entity<SyncedOrderLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Sku).HasMaxLength(100);
            e.Property(x => x.Title).HasMaxLength(500).IsRequired();
            e.Property(x => x.Price).HasPrecision(18, 2);
        });

        modelBuilder.Entity<SyncedCustomer>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.FirstName).HasMaxLength(200);
            e.Property(x => x.LastName).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(50);
            e.Property(x => x.TotalSpent).HasPrecision(18, 2);
            e.HasIndex(x => new { x.TenantId, x.ShopifyCustomerId }).IsUnique();
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
