using Marketplace.BuildingBlocks.Domain;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.Cms.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Cms.Api.Infrastructure;

public class CmsDbContext : DbContext
{
    private readonly ITenantContext _tenant;

    public CmsDbContext(DbContextOptions<CmsDbContext> options, ITenantContext tenant)
        : base(options)
        => _tenant = tenant;

    public DbSet<Page> Pages => Set<Page>();
    public DbSet<PageVersion> PageVersions => Set<PageVersion>();
    public DbSet<PageComponent> PageComponents => Set<PageComponent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("cms");

        modelBuilder.Entity<Page>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(300).IsRequired();
            e.Property(x => x.Handle).HasMaxLength(200).IsRequired();
            e.Property(x => x.ScreenType).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => new { x.TenantId, x.Handle }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.ScreenType });
            e.HasMany(x => x.Versions).WithOne().HasForeignKey(v => v.PageId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenant.IsPlatformScope || x.TenantId == _tenant.TenantId);
        });

        modelBuilder.Entity<PageVersion>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PublishedBy).HasMaxLength(200);
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasIndex(x => new { x.PageId, x.VersionNumber }).IsUnique();
            e.HasMany(x => x.Components).WithOne().HasForeignKey(c => c.PageVersionId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenant.IsPlatformScope || x.TenantId == _tenant.TenantId);
        });

        modelBuilder.Entity<PageComponent>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(50).IsRequired();
            // Ayarlar tip bazlı değiştiği için jsonb: şemasız saklanır, uygulamada doğrulanır.
            e.Property(x => x.SettingsJson).HasColumnType("jsonb").IsRequired();
            e.HasIndex(x => new { x.PageVersionId, x.Position });
        });

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
