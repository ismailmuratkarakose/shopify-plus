using Marketplace.BuildingBlocks.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Marketplace.Cms.Api.Infrastructure;

public sealed class DesignTimeCmsDbContextFactory : IDesignTimeDbContextFactory<CmsDbContext>
{
    public CmsDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("CMS_DB")
                   ?? "Host=localhost;Port=5432;Database=cms;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<CmsDbContext>().UseNpgsql(conn).Options;
        var tenant = new TenantContext();
        tenant.SetTenant(null, isPlatformScope: true);
        return new CmsDbContext(options, tenant);
    }
}
