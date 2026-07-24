using Marketplace.BuildingBlocks.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Marketplace.Catalog.Api.Infrastructure;

/// <summary>dotnet ef migrations için tasarım zamanı fabrikası (çalışan DB gerekmez).</summary>
public sealed class DesignTimeCatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("CATALOG_DB")
                   ?? "Host=localhost;Port=5432;Database=catalog;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(conn).Options;
        var scope = new StoreContext();
        scope.SetStore(null, isPlatformScope: true);
        return new CatalogDbContext(options, scope);
    }
}
