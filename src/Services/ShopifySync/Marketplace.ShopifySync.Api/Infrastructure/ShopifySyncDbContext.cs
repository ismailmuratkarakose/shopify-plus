using Marketplace.BuildingBlocks.Domain;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Outbox;
using Marketplace.ShopifySync.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.ShopifySync.Api.Infrastructure;

public class ShopifySyncDbContext : DbContext
{
    private readonly IStoreContext _scope;

    public ShopifySyncDbContext(DbContextOptions<ShopifySyncDbContext> options, IStoreContext scope)
        : base(options)
        => _scope = scope;

    public DbSet<ShopifyIntegration> Integrations => Set<ShopifyIntegration>();
    public DbSet<ProductMapping> ProductMappings => Set<ProductMapping>();
    public DbSet<WebhookInbox> WebhookInbox => Set<WebhookInbox>();
    public DbSet<SyncedProduct> SyncedProducts => Set<SyncedProduct>();
    public DbSet<SyncedCollection> SyncedCollections => Set<SyncedCollection>();
    public DbSet<SyncedOrder> SyncedOrders => Set<SyncedOrder>();
    public DbSet<SyncedCustomer> SyncedCustomers => Set<SyncedCustomer>();
    public DbSet<SyncedDiscount> SyncedDiscounts => Set<SyncedDiscount>();
    public DbSet<SyncedPage> SyncedPages => Set<SyncedPage>();
    public DbSet<StoreSyncState> SyncStates => Set<StoreSyncState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("shopify");

        modelBuilder.Entity<ShopifyIntegration>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ShopDomain).HasMaxLength(255).IsRequired();
            e.HasIndex(x => x.StoreId).IsUnique(); // merchant başına tek Shopify entegrasyonu
            e.HasQueryFilter(x => _scope.IsPlatformScope || x.StoreId == _scope.StoreId);
        });

        modelBuilder.Entity<ProductMapping>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Sku).HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.StoreId, x.Sku }).IsUnique();
            e.HasQueryFilter(x => _scope.IsPlatformScope || x.StoreId == _scope.StoreId);
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
            e.HasIndex(x => new { x.StoreId, x.ShopifyProductId }).IsUnique();
            e.HasMany(x => x.Variants).WithOne().HasForeignKey(v => v.SyncedProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _scope.IsPlatformScope || x.StoreId == _scope.StoreId);
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
            e.HasIndex(x => new { x.StoreId, x.ShopifyCollectionId }).IsUnique();
            e.HasMany(x => x.Products).WithOne().HasForeignKey(p => p.SyncedCollectionId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _scope.IsPlatformScope || x.StoreId == _scope.StoreId);
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
            e.HasIndex(x => new { x.StoreId, x.ShopifyOrderId }).IsUnique();
            e.HasIndex(x => new { x.StoreId, x.ShopifyCustomerId });
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.SyncedOrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _scope.IsPlatformScope || x.StoreId == _scope.StoreId);
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
            e.HasIndex(x => new { x.StoreId, x.ShopifyCustomerId }).IsUnique();
            e.HasQueryFilter(x => _scope.IsPlatformScope || x.StoreId == _scope.StoreId);
        });

        modelBuilder.Entity<SyncedDiscount>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Code).HasMaxLength(100).IsRequired();
            e.Property(x => x.DiscountType).HasMaxLength(30).IsRequired();
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Status).HasMaxLength(30).IsRequired();
            e.Property(x => x.Value).HasPrecision(18, 2);
            e.HasIndex(x => new { x.StoreId, x.ShopifyDiscountId }).IsUnique();
            e.HasQueryFilter(x => _scope.IsPlatformScope || x.StoreId == _scope.StoreId);
        });

        modelBuilder.Entity<SyncedPage>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(500).IsRequired();
            e.Property(x => x.Handle).HasMaxLength(300).IsRequired();
            e.Property(x => x.Status).HasMaxLength(30).IsRequired();
            e.HasIndex(x => new { x.StoreId, x.ShopifyPageId }).IsUnique();
            e.HasQueryFilter(x => _scope.IsPlatformScope || x.StoreId == _scope.StoreId);
        });

        modelBuilder.Entity<StoreSyncState>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.LastStatus).HasMaxLength(20).IsRequired();
            e.Property(x => x.LastTrigger).HasMaxLength(20).IsRequired();
            e.Property(x => x.LastError).HasMaxLength(2000);
            e.HasIndex(x => x.StoreId).IsUnique();   // mağaza başına tek durum kaydı
            e.HasQueryFilter(x => _scope.IsPlatformScope || x.StoreId == _scope.StoreId);
        });

        modelBuilder.AddOutboxMessage("shopify");
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
