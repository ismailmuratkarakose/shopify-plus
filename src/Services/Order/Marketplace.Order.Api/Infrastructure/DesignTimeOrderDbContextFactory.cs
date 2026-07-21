using Marketplace.BuildingBlocks.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Marketplace.Order.Api.Infrastructure;

public sealed class DesignTimeOrderDbContextFactory : IDesignTimeDbContextFactory<OrderDbContext>
{
    public OrderDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("ORDER_DB")
                   ?? "Host=localhost;Port=5432;Database=ordering;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<OrderDbContext>().UseNpgsql(conn).Options;
        var tenant = new TenantContext();
        tenant.SetTenant(null, isPlatformScope: true);
        return new OrderDbContext(options, tenant);
    }
}
