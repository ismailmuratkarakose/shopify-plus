using Marketplace.BuildingBlocks.Auditing;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Web;
using Marketplace.Catalog.Api.Domain;
using Marketplace.Catalog.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Catalog.Api.Api;

public record StoreOfferDto(Guid OfferId, Guid ProductId, string Barcode, string Title, string? Brand,
    Guid? CategoryId, string? Sku, decimal Price, decimal? CompareAtPrice, string Currency,
    int StockQuantity, bool IsActive, string Source, DateTimeOffset CreatedAt);

/// <summary>
/// Mağazanın ürün ekleme isteği. Barkod master'ı belirler: kart yoksa bu bilgilerle OLUŞUR,
/// varsa mağaza mevcut karta kendi teklifiyle KATILIR (kartın alanları değişmez).
/// </summary>
public record UpsertStoreProductRequest(string Barcode, string Title, decimal Price,
    string? Description = null, string? Brand = null, Guid? CategoryId = null, string? ImageUrl = null,
    string? Sku = null, decimal? CompareAtPrice = null, int StockQuantity = 0);

public record UpdateOfferRequest(decimal Price, decimal? CompareAtPrice, int StockQuantity,
    string? Sku, bool IsActive);

/// <summary>
/// Mağazanın kendi katalog yönetimi (manuel giriş — R3'te Excel içeri aktarım eklenir).
/// Shopify zorunlu DEĞİLDİR: mağaza ürünlerini buradan tek tek veya Excel'le yönetebilir.
/// </summary>
public static class StoreProductEndpoints
{
    public static IEndpointRouteBuilder MapStoreProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/store/products").WithTags("StoreProducts")
            .RequireAuthorization(Policies.StoreManage);

        group.MapGet("/", async (CatalogDbContext db, CancellationToken ct,
            string? q = null, int page = 1, int pageSize = 50) =>
        {
            var p = Math.Max(1, page);
            var size = Math.Clamp(pageSize, 1, 200);

            // Mağaza filtresi query filter'dan gelir: mağaza yalnızca kendi tekliflerini görür.
            var query = db.Offers.Include(o => o.Product).AsQueryable();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = $"%{q.Trim()}%";
                query = query.Where(o => EF.Functions.ILike(o.Product.Title, term)
                                         || o.Product.Barcode == q.Trim()
                                         || o.Sku == q.Trim());
            }

            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(o => o.CreatedAt)
                .Skip((p - 1) * size).Take(size)
                .Select(o => ToDto(o))
                .ToListAsync(ct);
            return Results.Ok(new { total, page = p, pageSize = size, items });
        });

        group.MapPost("/", async (UpsertStoreProductRequest req, CatalogDbContext db, IStoreContext scope,
            Application.ProductUpsertService upsert, IAuditLogger audit, CancellationToken ct) =>
        {
            if (scope.StoreId is not { } storeId)
                return Results.Problem("Mağaza kapsamı yok. (Platform personeli X-Acting-Store başlığı kullanmalı.)",
                    statusCode: StatusCodes.Status400BadRequest, title: "store.missing");

            if (req.CategoryId is { } cid && !await db.Categories.AnyAsync(c => c.Id == cid && c.IsActive, ct))
                return Results.Problem("Kategori bulunamadı.", statusCode: StatusCodes.Status400BadRequest,
                    title: "product.category_not_found");

            var input = new Application.UpsertInput(req.Barcode, req.Title, req.Price, req.Description,
                req.Brand, req.CategoryId, req.ImageUrl, req.Sku, req.CompareAtPrice, req.StockQuantity);
            var (outcome, error) = await upsert.UpsertAsync(input, ProductSource.Manual, ct);
            if (error is not null)
                return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest, title: "product.invalid");

            var (product, offer, createdMaster, createdOffer) = (outcome!.Product, outcome.Offer,
                outcome.CreatedMaster, outcome.CreatedOffer);

            audit.Record(createdOffer ? "offer.create" : "offer.update",
                $"'{product.Title}' ({product.Barcode}) — fiyat {req.Price} {offer.Currency}, stok {req.StockQuantity}" +
                (createdMaster ? " [yeni ürün kartı]" : " [mevcut karta katılım]"),
                "Offer", offer.Id.ToString());
            await db.SaveChangesAsync(ct);

            var dto = new StoreOfferDto(offer.Id, product.Id, product.Barcode, product.Title, product.Brand,
                product.CategoryId, offer.Sku, offer.Price, offer.CompareAtPrice, offer.Currency,
                offer.StockQuantity, offer.IsActive, offer.Source, offer.CreatedAt);
            return createdOffer ? Results.Created($"/api/store/products/{offer.Id}", dto) : Results.Ok(dto);
        });

        group.MapPut("/{offerId:guid}", async (Guid offerId, UpdateOfferRequest req, CatalogDbContext db,
            IAuditLogger audit, CancellationToken ct) =>
        {
            var offer = await db.Offers.Include(o => o.Product).FirstOrDefaultAsync(o => o.Id == offerId, ct);
            if (offer is null) return Results.NotFound();
            if (req.Price <= 0)
                return Results.Problem("Fiyat sıfırdan büyük olmalı.", statusCode: StatusCodes.Status400BadRequest,
                    title: "product.price_invalid");

            offer.Price = req.Price;
            offer.CompareAtPrice = req.CompareAtPrice;
            offer.StockQuantity = req.StockQuantity;
            offer.Sku = req.Sku;
            offer.IsActive = req.IsActive;
            audit.Record("offer.update", $"'{offer.Product.Title}' teklifi güncellendi — fiyat {req.Price}, stok {req.StockQuantity}",
                "Offer", offer.Id.ToString());
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(offer));
        });

        group.MapDelete("/{offerId:guid}", async (Guid offerId, CatalogDbContext db, IAuditLogger audit,
            CancellationToken ct) =>
        {
            var offer = await db.Offers.Include(o => o.Product).FirstOrDefaultAsync(o => o.Id == offerId, ct);
            if (offer is null) return Results.NotFound();

            // Teklif silinmez, pasifleşir: sipariş geçmişi ve kart bütünlüğü korunur.
            offer.IsActive = false;
            audit.Record("offer.deactivate", $"'{offer.Product.Title}' teklifi satıştan kaldırıldı",
                "Offer", offer.Id.ToString());
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { deactivated = true });
        });

        return app;
    }

    private static StoreOfferDto ToDto(Offer o) =>
        new(o.Id, o.ProductId, o.Product.Barcode, o.Product.Title, o.Product.Brand, o.Product.CategoryId,
            o.Sku, o.Price, o.CompareAtPrice, o.Currency, o.StockQuantity, o.IsActive, o.Source, o.CreatedAt);
}
