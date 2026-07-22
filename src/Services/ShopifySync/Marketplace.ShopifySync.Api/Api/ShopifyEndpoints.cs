using Marketplace.BuildingBlocks.Web;
using Marketplace.ShopifySync.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.ShopifySync.Api.Api;

public static class ShopifyEndpoints
{
    public static IEndpointRouteBuilder MapShopifyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/shopify")
            .RequireAuthorization()
            .WithTags("ShopifySync");

        // Aktif merchant'ın Shopify entegrasyon durumu (token maskeli).
        group.MapGet("/integration", async (ShopifySyncDbContext db) =>
        {
            var i = await db.Integrations.FirstOrDefaultAsync();
            return i is null
                ? Results.NotFound(new { message = "Shopify entegrasyonu yok." })
                : Results.Ok(new { i.ShopDomain, i.IsActive, tokenStored = !string.IsNullOrEmpty(i.EncryptedAccessToken) });
        });

        return app;
    }
}
