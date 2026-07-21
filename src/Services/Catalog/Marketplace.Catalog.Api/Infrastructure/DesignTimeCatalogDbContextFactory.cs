using Marketplace.BuildingBlocks.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Marketplace.Catalog.Api.Infrastructure;

/// <summary>
/// `dotnet ef migrations` komutları için tasarım-zamanı context üretici.
/// Migration üretiminde gerçek tenant çözümü gerekmez; platform kapsamlı boş bir context yeterli.
/// </summary>
public sealed class DesignTimeCatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("CATALOG_DB")
                   ?? "Host=localhost;Port=5432;Database=catalog;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(conn)
            .Options;

        var tenant = new TenantContext();
        tenant.SetTenant(null, isPlatformScope: true);
        return new CatalogDbContext(options, tenant);
    }
}
