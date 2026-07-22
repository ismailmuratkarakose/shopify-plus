using Marketplace.Payment.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Payment.Api.Api;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/payments")
            .RequireAuthorization()
            .WithTags("Payments");

        group.MapGet("/", async (PaymentDbContext db) =>
        {
            var items = await db.Payments
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new { p.Id, p.OrderId, p.Amount, p.Currency, p.Provider, Status = p.Status.ToString(), p.ProviderPaymentId, p.FailureReason })
                .ToListAsync();
            return Results.Ok(items);
        });

        return app;
    }
}
