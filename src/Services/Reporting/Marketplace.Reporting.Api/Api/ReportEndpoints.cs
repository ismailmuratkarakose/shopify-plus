using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.Reporting.Api.Domain;
using Marketplace.Reporting.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Reporting.Api.Api;

// --- Rapor DTO'ları ---
public record SummaryDto(string Scope, int Orders, int PaidOrders, decimal GrossRevenue, decimal TotalCommission, decimal NetToMerchants);
public record MerchantReportDto(Guid TenantId, string Name, decimal CommissionRate, int Orders, int PaidOrders, decimal GrossRevenue, decimal Commission, decimal Net);
public record DailyPointDto(DateOnly Date, int Orders, decimal Revenue, decimal Commission);

public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports").RequireAuthorization().WithTags("Reports");

        // Kapsam özeti: owner tüm platformu, merchant kendi verisini görür.
        group.MapGet("/summary", async (ReportingDbContext db, ITenantContext tenant, CancellationToken ct) =>
        {
            var scoped = Scope(db, tenant);
            if (scoped is null) return Forbidden();

            var paid = scoped.Where(s => s.Status == SaleStatus.Paid);
            var orders = await scoped.CountAsync(ct);
            var paidOrders = await paid.CountAsync(ct);
            var gross = await paid.SumAsync(s => (decimal?)s.Amount, ct) ?? 0m;
            var commission = await paid.SumAsync(s => (decimal?)s.CommissionAmount, ct) ?? 0m;

            var scope = tenant.IsPlatformScope ? "platform" : "merchant";
            return Results.Ok(new SummaryDto(scope, orders, paidOrders, gross, commission, gross - commission));
        });

        // Merchant kırılımı: yalnızca owner/platform.
        group.MapGet("/merchants", async (ReportingDbContext db, ITenantContext tenant, CancellationToken ct) =>
        {
            if (!tenant.IsPlatformScope) return Forbidden();

            var rates = await db.MerchantRates.ToListAsync(ct);
            var agg = await db.Sales
                .GroupBy(s => s.TenantId)
                .Select(g => new
                {
                    TenantId = g.Key,
                    Orders = g.Count(),
                    PaidOrders = g.Sum(x => x.Status == SaleStatus.Paid ? 1 : 0),
                    Gross = g.Sum(x => x.Status == SaleStatus.Paid ? x.Amount : 0m),
                    Commission = g.Sum(x => x.Status == SaleStatus.Paid ? x.CommissionAmount : 0m)
                })
                .ToListAsync(ct);
            var byTenant = agg.ToDictionary(a => a.TenantId);

            var result = rates.Select(r =>
            {
                byTenant.TryGetValue(r.TenantId, out var a);
                var gross = a?.Gross ?? 0m;
                var commission = a?.Commission ?? 0m;
                return new MerchantReportDto(r.TenantId, r.Name, r.CommissionRate,
                    a?.Orders ?? 0, a?.PaidOrders ?? 0, gross, commission, gross - commission);
            })
            .OrderByDescending(x => x.GrossRevenue)
            .ToList();

            return Results.Ok(result);
        });

        // Günlük ciro/komisyon serisi (ödenen siparişler, PaidAt gününe göre).
        group.MapGet("/daily", async (ReportingDbContext db, ITenantContext tenant, CancellationToken ct, int days = 30) =>
        {
            var scoped = Scope(db, tenant);
            if (scoped is null) return Forbidden();

            var since = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(days, 1, 365));
            var rows = await scoped
                .Where(s => s.Status == SaleStatus.Paid && s.PaidAt >= since)
                .Select(s => new { s.PaidAt, s.Amount, s.CommissionAmount })
                .ToListAsync(ct);

            var points = rows
                .GroupBy(r => DateOnly.FromDateTime(r.PaidAt!.Value.UtcDateTime))
                .Select(g => new DailyPointDto(g.Key, g.Count(), g.Sum(x => x.Amount), g.Sum(x => x.CommissionAmount)))
                .OrderBy(p => p.Date)
                .ToList();

            return Results.Ok(points);
        });

        return app;
    }

    /// <summary>Sorguyu kapsamı ile daraltır: owner→hepsi, merchant→kendi; kapsam yoksa null (403).</summary>
    private static IQueryable<SalesFact>? Scope(ReportingDbContext db, ITenantContext tenant)
    {
        if (tenant.IsPlatformScope) return db.Sales;
        if (tenant.TenantId is { } tid) return db.Sales.Where(s => s.TenantId == tid);
        return null;
    }

    private static IResult Forbidden() =>
        Results.Problem("Bu rapor için yetkiniz yok.", statusCode: StatusCodes.Status403Forbidden, title: "reports.forbidden");
}
