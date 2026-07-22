using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Marketplace.Reporting.Api.Infrastructure;

public sealed class DesignTimeReportingDbContextFactory : IDesignTimeDbContextFactory<ReportingDbContext>
{
    public ReportingDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("REPORTING_DB")
                   ?? "Host=localhost;Port=5432;Database=reporting;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<ReportingDbContext>().UseNpgsql(conn).Options;
        return new ReportingDbContext(options);
    }
}
