using Marketplace.BuildingBlocks.Auditing;
using Marketplace.BuildingBlocks.Domain;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Outbox;
using Marketplace.Merchant.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Merchant.Api.Infrastructure;

public class MerchantDbContext : DbContext
{
    private readonly IStoreContext _scope;

    public MerchantDbContext(DbContextOptions<MerchantDbContext> options, IStoreContext scope)
        : base(options)
        => _scope = scope;

    public DbSet<Domain.Merchant> Merchants => Set<Domain.Merchant>();
    public DbSet<MerchantIntegration> Integrations => Set<MerchantIntegration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("merchant");

        modelBuilder.Entity<Domain.Merchant>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(220).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.CommissionRate).HasPrecision(5, 4);
            e.HasIndex(x => x.Slug).IsUnique();
            // Merchant = tenant. Owner tüm merchant'ları görür; merchant yalnızca kendini.
            e.HasQueryFilter(x => _scope.IsPlatformScope || x.Id == _scope.StoreId);
        });

        modelBuilder.Entity<MerchantIntegration>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Provider).HasMaxLength(20).IsRequired();
            e.Property(x => x.EncryptedConfig).IsRequired();
            e.HasIndex(x => new { x.StoreId, x.Provider }).IsUnique();
            e.HasQueryFilter(x => _scope.IsPlatformScope || x.StoreId == _scope.StoreId);
        });

        modelBuilder.AddOutboxMessage("merchant");

        modelBuilder.AddAuditLog(x => _scope.IsPlatformScope || x.StoreId == _scope.StoreId, "merchant");

        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyStoreAndAudit();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyStoreAndAudit()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is IStoreOwned owned &&
                entry.State == EntityState.Added &&
                owned.StoreId == Guid.Empty &&
                _scope.StoreId is { } tid)
            {
                owned.StoreId = tid;
            }

            if (entry.Entity is IAuditable audit && entry.State == EntityState.Modified)
                audit.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }
}
