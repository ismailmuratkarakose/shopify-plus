using Marketplace.Catalog.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Catalog.Api.Api;

public record PublicProductListItem(Guid ProductId, string Barcode, string Title, string? Brand,
    string? ImageUrl, Guid? CategoryId, decimal? BestPrice, string? Currency, int SellerCount, bool InStock);

public record PublicOfferDto(Guid OfferId, Guid StoreId, decimal Price, decimal? CompareAtPrice,
    string Currency, int StockQuantity, bool InStock);

public record PublicProductDetail(Guid ProductId, string Barcode, string Title, string? Description,
    string? Brand, string? ImageUrl, Guid? CategoryId, IReadOnlyList<PublicOfferDto> Offers);

/// <summary>
/// Kamusal katalog okumaları — pazaryeri müşterisi (giriş yapmış ya da yapmamış) için.
/// Satıcı kıyası burada olur: tek ürün kartı, N mağazanın teklifi, en iyi fiyat önde.
/// Teklif sorguları mağaza filtresini bilerek atlar (IgnoreQueryFilters): vitrin tüm mağazaları görür.
/// </summary>
public static class PublicCatalogEndpoints
{
    public static IEndpointRouteBuilder MapPublicCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/catalog/products").WithTags("PublicCatalog").AllowAnonymous();

        group.MapGet("/", async (CatalogDbContext db, CancellationToken ct,
            string? q = null, Guid? categoryId = null, string sort = "relevance",
            int page = 1, int pageSize = 20) =>
        {
            var p = Math.Max(1, page);
            var size = Math.Clamp(pageSize, 1, 100);

            // IgnoreQueryFilters ŞART: Offer'daki mağaza filtresi navigation üzerinden de uygulanır;
            // anonim istekte (StoreId=null) tüm teklifler elenir ve vitrin boş kalırdı.
            var products = db.Products.IgnoreQueryFilters().Where(x => x.IsActive);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = $"%{q.Trim()}%";
                products = products.Where(x =>
                    EF.Functions.ILike(x.Title, term) ||
                    EF.Functions.ILike(x.Brand ?? "", term) ||
                    x.Barcode == q.Trim());
            }
            if (categoryId is { } cid)
                products = products.Where(x => x.CategoryId == cid);

            // Vitrinde yalnızca en az bir AKTİF teklifi olan kartlar görünür.
            var query = products.Select(x => new
            {
                x.Id, x.Barcode, x.Title, x.Brand, x.ImageUrl, x.CategoryId,
                BestPrice = x.Offers.Where(o => o.IsActive).Min(o => (decimal?)o.Price),
                Currency = x.Offers.Where(o => o.IsActive).Select(o => o.Currency).FirstOrDefault(),
                SellerCount = x.Offers.Count(o => o.IsActive),
                InStock = x.Offers.Any(o => o.IsActive && o.StockQuantity > 0)
            }).Where(x => x.SellerCount > 0);

            query = sort switch
            {
                "price_asc" => query.OrderBy(x => x.BestPrice),
                "price_desc" => query.OrderByDescending(x => x.BestPrice),
                "newest" => query.OrderByDescending(x => x.Id),
                _ => query.OrderBy(x => x.Title)
            };

            var total = await query.CountAsync(ct);
            var items = await query.Skip((p - 1) * size).Take(size)
                .Select(x => new PublicProductListItem(x.Id, x.Barcode, x.Title, x.Brand, x.ImageUrl,
                    x.CategoryId, x.BestPrice, x.Currency, x.SellerCount, x.InStock))
                .ToListAsync(ct);

            return Results.Ok(new { total, page = p, pageSize = size, items });
        });

        group.MapGet("/{id:guid}", async (Guid id, CatalogDbContext db, CancellationToken ct) =>
        {
            var product = await db.Products.FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
            if (product is null) return Results.NotFound();

            // Satıcı kıyası: tüm mağazaların aktif teklifleri, en ucuz önde.
            var offers = await db.Offers.IgnoreQueryFilters()
                .Where(o => o.ProductId == id && o.IsActive)
                .OrderBy(o => o.Price)
                .Select(o => new PublicOfferDto(o.Id, o.StoreId, o.Price, o.CompareAtPrice,
                    o.Currency, o.StockQuantity, o.StockQuantity > 0))
                .ToListAsync(ct);

            return Results.Ok(new PublicProductDetail(product.Id, product.Barcode, product.Title,
                product.Description, product.Brand, product.ImageUrl, product.CategoryId, offers));
        });

        group.MapGet("/barcode/{barcode}", async (string barcode, CatalogDbContext db, CancellationToken ct) =>
        {
            var product = await db.Products.FirstOrDefaultAsync(x => x.Barcode == barcode && x.IsActive, ct);
            return product is null
                ? Results.NotFound()
                : Results.Redirect($"/api/catalog/products/{product.Id}");
        });

        return app;
    }
}
