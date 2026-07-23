using Marketplace.BuildingBlocks.Auditing;
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
    public DbSet<PreviewToken> PreviewTokens => Set<PreviewToken>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<ExperienceSnapshot> ExperienceSnapshots => Set<ExperienceSnapshot>();

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

        modelBuilder.Entity<PreviewToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Token).HasMaxLength(64).IsRequired();
            e.Property(x => x.CreatedBy).HasMaxLength(200);
            e.HasIndex(x => x.Token).IsUnique();
            e.HasIndex(x => x.PageId);
            // NOT: önizleme ucu anonimdir; kiracıyı anahtarın kendisi belirler → burada tenant filtresi YOK.
        });

        modelBuilder.Entity<MediaAsset>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.FileName).HasMaxLength(300).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
            e.Property(x => x.StoragePath).HasMaxLength(500).IsRequired();
            e.Property(x => x.UploadedBy).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.CreatedAt });
            e.HasQueryFilter(x => _tenant.IsPlatformScope || x.TenantId == _tenant.TenantId);
        });

        modelBuilder.Entity<FeatureFlag>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).HasMaxLength(100).IsRequired();
            e.Property(x => x.Value).HasMaxLength(500);
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
            e.HasQueryFilter(x => _tenant.IsPlatformScope || x.TenantId == _tenant.TenantId);
        });

        modelBuilder.Entity<ExperienceSnapshot>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Json).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.GeneratedBy).HasMaxLength(200);
            e.Property(x => x.Reason).HasMaxLength(30).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Version }).IsUnique();
            e.HasQueryFilter(x => _tenant.IsPlatformScope || x.TenantId == _tenant.TenantId);
        });

        modelBuilder.AddAuditLog(x => _tenant.IsPlatformScope || x.TenantId == _tenant.TenantId, "cms");

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
