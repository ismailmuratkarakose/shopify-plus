using Marketplace.BuildingBlocks.Domain;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Outbox;
using Marketplace.Inventory.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Inventory.Api.Infrastructure;

public class InventoryDbContext : DbContext
{
    private readonly ITenantContext _tenant;

    public InventoryDbContext(DbContextOptions<InventoryDbContext> options, ITenantContext tenant)
        : base(options)
        => _tenant = tenant;

    public DbSet<InventoryItem> Items => Set<InventoryItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("inventory");

        modelBuilder.Entity<InventoryItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Sku).HasMaxLength(100).IsRequired();
            e.Ignore(x => x.Available);
            e.HasIndex(x => new { x.TenantId, x.ProductId }).IsUnique();
            e.HasQueryFilter(x => _tenant.IsPlatformScope || x.TenantId == _tenant.TenantId);
        });

        modelBuilder.AddOutboxMessage("inventory");
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
