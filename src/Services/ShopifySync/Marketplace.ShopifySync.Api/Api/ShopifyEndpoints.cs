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

        // Pazaryeri ↔ Shopify ürün eşlemeleri.
        group.MapGet("/mappings", async (ShopifySyncDbContext db) =>
        {
            var maps = await db.ProductMappings
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => new { m.MarketplaceProductId, m.ShopifyProductId, m.Sku })
                .ToListAsync();
            return Results.Ok(maps);
        });

        return app;
    }
}
