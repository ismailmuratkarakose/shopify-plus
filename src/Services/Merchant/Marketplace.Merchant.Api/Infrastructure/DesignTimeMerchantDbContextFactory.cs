using Marketplace.BuildingBlocks.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Marketplace.Merchant.Api.Infrastructure;

public sealed class DesignTimeMerchantDbContextFactory : IDesignTimeDbContextFactory<MerchantDbContext>
{
    public MerchantDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("MERCHANT_DB")
                   ?? "Host=localhost;Port=5432;Database=merchant;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<MerchantDbContext>()
            .UseNpgsql(conn)
            .Options;

        var tenant = new TenantContext();
        tenant.SetTenant(null, isPlatformScope: true);
        return new MerchantDbContext(options, tenant);
    }
}
