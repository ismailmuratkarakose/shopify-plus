using Marketplace.BuildingBlocks.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Marketplace.Inventory.Api.Infrastructure;

public sealed class DesignTimeInventoryDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("INVENTORY_DB")
                   ?? "Host=localhost;Port=5432;Database=inventory;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<InventoryDbContext>().UseNpgsql(conn).Options;
        var tenant = new TenantContext();
        tenant.SetTenant(null, isPlatformScope: true);
        return new InventoryDbContext(options, tenant);
    }
}
