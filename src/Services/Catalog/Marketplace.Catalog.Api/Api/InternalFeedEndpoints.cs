using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.Catalog.Api.Application;
using Marketplace.Catalog.Api.Domain;
using Marketplace.Catalog.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Catalog.Api.Api;

public record FeedItem(string? Barcode, string Title, decimal Price, int StockQuantity,
    string? Description, string? Brand, string? ImageUrl, string? Sku, decimal? CompareAtPrice,
    long ShopifyProductId, long ShopifyVariantId);

public record ShopifyFeedRequest(Guid StoreId, List<FeedItem> Items);

public record ShopifyFeedResult(int Received, int Imported, int NewProducts, int Deactivated,
    IReadOnlyList<string> Errors);

/// <summary>
/// Shopify besleyici ucu (R4): ShopifySync her senkron sonrası mağazanın Shopify ürünlerini buraya
/// TAM LİSTE olarak iter. Barkod master'ı belirler; teklif source=shopify ile beslenir.
/// Bildirim TAM DURUM olduğundan, bu mağazanın listede olmayan shopify-kaynaklı teklifleri
/// satıştan kaldırılır (Shopify'da silinen ürün vitrinde kalmasın).
///
/// GÜVENLİK: yalnızca servisler arası ağdan çağrılır — gateway'e ROTALANMAZ (internal desen,
/// Merchant /internal ile aynı). Kimlik taşımaz; mağaza kapsamı gövdeden kurulur.
/// </summary>
public static class InternalFeedEndpoints
{
    public static IEndpointRouteBuilder MapInternalFeedEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/internal/feed/shopify", async (ShopifyFeedRequest req, CatalogDbContext db,
            ProductUpsertService upsert, IStoreContext scope, ILogger<ShopifyFeedRequest> logger,
            CancellationToken ct) =>
        {
            if (req.StoreId == Guid.Empty)
                return Results.Problem("StoreId gerekli.", statusCode: StatusCodes.Status400BadRequest,
                    title: "feed.store_required");

            // İç çağrı JWT taşımaz → mağaza kapsamı gövdeden kurulur (query filter + StoreId ataması için).
            scope.SetStore(req.StoreId, isPlatformScope: false);

            var errors = new List<string>();
            var imported = 0; var newProducts = 0;
            var seenVariantIds = new HashSet<long>();

            foreach (var item in req.Items)
            {
                var input = new UpsertInput(item.Barcode, item.Title, item.Price, item.Description,
                    item.Brand, null, item.ImageUrl, item.Sku, item.CompareAtPrice, item.StockQuantity);
                var (outcome, error) = await upsert.UpsertAsync(input, ProductSource.Shopify, ct);
                if (error is not null)
                {
                    errors.Add($"variant {item.ShopifyVariantId}: {error}");
                    continue;
                }

                outcome!.Offer.ShopifyProductId = item.ShopifyProductId;
                outcome.Offer.ShopifyVariantId = item.ShopifyVariantId;
                outcome.Offer.LastSyncedAt = DateTimeOffset.UtcNow;
                seenVariantIds.Add(item.ShopifyVariantId);
                imported++;
                if (outcome.CreatedMaster) newProducts++;
            }

            // Tam durum mutabakatı: bu senkronda görülmeyen shopify-kaynaklı teklifler satıştan kalkar.
            // Manuel/Excel teklifler DOKUNULMAZ — mağaza Shopify'ı yalnızca ek kaynak olarak kullanabilir.
            var stale = await db.Offers
                .Where(o => o.Source == ProductSource.Shopify && o.IsActive &&
                            (o.ShopifyVariantId == null || !seenVariantIds.Contains(o.ShopifyVariantId.Value)))
                .ToListAsync(ct);
            foreach (var s in stale) s.IsActive = false;

            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Shopify beslemesi: store={Store} gelen={Received} aktarılan={Imported} yeniKart={New} pasifleşen={Stale} hata={Err}",
                req.StoreId, req.Items.Count, imported, newProducts, stale.Count, errors.Count);

            return Results.Ok(new ShopifyFeedResult(req.Items.Count, imported, newProducts, stale.Count, errors));
        });

        return app;
    }
}
