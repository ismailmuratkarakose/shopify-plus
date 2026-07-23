using Marketplace.BuildingBlocks.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Marketplace.Mobile.Api.Infrastructure;

public sealed class DesignTimeMobileDbContextFactory : IDesignTimeDbContextFactory<MobileDbContext>
{
    public MobileDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("MOBILE_DB")
                   ?? "Host=localhost;Port=5432;Database=mobile;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<MobileDbContext>().UseNpgsql(conn).Options;
        var scope = new StoreContext();
        scope.SetStore(null, isPlatformScope: true);
        return new MobileDbContext(options, scope);
    }
}
