using Marketplace.BuildingBlocks.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Marketplace.ShopifySync.Api.Infrastructure;

public sealed class DesignTimeShopifySyncDbContextFactory : IDesignTimeDbContextFactory<ShopifySyncDbContext>
{
    public ShopifySyncDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("SHOPIFY_DB")
                   ?? "Host=localhost;Port=5432;Database=shopifysync;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<ShopifySyncDbContext>().UseNpgsql(conn).Options;
        var tenant = new TenantContext();
        tenant.SetTenant(null, isPlatformScope: true);
        return new ShopifySyncDbContext(options, tenant);
    }
}
