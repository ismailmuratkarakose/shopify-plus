using Marketplace.BuildingBlocks.Domain;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Outbox;
using Marketplace.Order.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Order.Api.Infrastructure;

public class OrderDbContext : DbContext
{
    private readonly ITenantContext _tenant;

    public OrderDbContext(DbContextOptions<OrderDbContext> options, ITenantContext tenant)
        : base(options)
        => _tenant = tenant;

    public DbSet<Domain.Order> Orders => Set<Domain.Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // "order" PostgreSQL'de rezerve kelime → şema "ordering".
        modelBuilder.HasDefaultSchema("ordering");

        modelBuilder.Entity<Domain.Order>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.TotalAmount).HasPrecision(18, 2);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenant.IsPlatformScope || x.TenantId == _tenant.TenantId);
        });

        modelBuilder.Entity<OrderItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Sku).HasMaxLength(100).IsRequired();
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Ignore(x => x.LineTotal);
        });

        modelBuilder.AddOutboxMessage("ordering");
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
