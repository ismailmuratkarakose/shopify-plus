using Marketplace.BuildingBlocks.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Marketplace.Payment.Api.Infrastructure;

public sealed class DesignTimePaymentDbContextFactory : IDesignTimeDbContextFactory<PaymentDbContext>
{
    public PaymentDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("PAYMENT_DB")
                   ?? "Host=localhost;Port=5432;Database=payment;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<PaymentDbContext>().UseNpgsql(conn).Options;
        var tenant = new TenantContext();
        tenant.SetTenant(null, isPlatformScope: true);
        return new PaymentDbContext(options, tenant);
    }
}
