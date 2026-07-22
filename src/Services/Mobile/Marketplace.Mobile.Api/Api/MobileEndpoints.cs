using System.Security.Claims;
using System.Text.Json.Nodes;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.Mobile.Api.Clients;
using Marketplace.Mobile.Api.Domain;
using Marketplace.Mobile.Api.Experience;
using Marketplace.Mobile.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Mobile.Api.Api;

public record MobileProductDto(long ProductId, string Title, string Handle, string? Vendor, string? ProductType,
    string? ImageUrl, decimal Price, decimal? CompareAtPrice, bool InStock, int TotalStock);
public record MobileVariantDto(long VariantId, string? Sku, string? Barcode, decimal Price,
    decimal? CompareAtPrice, int InventoryQuantity, string? Title);
public record MobileProductDetailDto(long ProductId, string Title, string Handle, string? Description,
    string? Vendor, string? ProductType, string? ImageUrl, decimal Price, decimal? CompareAtPrice,
    bool InStock, IReadOnlyList<MobileVariantDto> Variants);
public record CheckoutItem(long VariantId, int Quantity);
public record CheckoutRequest(List<CheckoutItem> Items);
public record FavoriteRequest(long ProductId);

/// <summary>
/// Mobil uygulamanın tükettiği tek giriş noktası: yayınlanan ekran yapılandırması (CMS snapshot'ı),
/// katalog/arama (Shopify read-model'i), kullanıcı listeleri ve Shopify Checkout yönlendirmesi.
/// Ödeme ve sipariş oluşturma Shopify tarafında yürür.
/// </summary>
public static class MobileEndpoints
{
    public static IEndpointRouteBuilder MapMobileEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/mobile").RequireAuthorization();

        MapExperience(api);
        MapCatalog(api);
        MapUserLists(api);
        MapCheckout(api);
        return app;
    }

    // --- Uzaktan yapılandırma / ekran deneyimi ---
    private static void MapExperience(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/experience").WithTags("Mobile.Experience");

        group.MapGet("/{screen}", async (string screen, HttpContext http, ITenantContext tenant,
            ExperienceService experience, CancellationToken ct) =>
        {
            if (tenant.TenantId is not { } tenantId) return Unauthorized();

            var snap = await experience.GetAsync(tenantId, ct);
            if (snap is null)
                return Results.Problem("Bu mağaza için yayınlanmış içerik yok.",
                    statusCode: StatusCodes.Status404NotFound, title: "experience.not_published");

            var page = ExperienceService.ResolveScreen(snap.Root, screen);
            if (page is null)
                return Results.Problem($"Ekran bulunamadı: '{screen}'",
                    statusCode: StatusCodes.Status404NotFound, title: "screen.not_found");

            // Sürüm + ekran bazlı ETag: içerik değişmedikçe mobil taraf gövdeyi tekrar indirmez.
            var etag = $"\"v{snap.Version}-{screen.ToLowerInvariant()}\"";
            if (http.Request.Headers.IfNoneMatch.ToString() == etag)
                return Results.StatusCode(StatusCodes.Status304NotModified);

            http.Response.Headers.ETag = etag;
            return Results.Ok(new
            {
                experienceVersion = snap.Version,
                screen = page["screenType"]?.GetValue<string>(),
                handle = page["handle"]?.GetValue<string>(),
                name = page["name"]?.GetValue<string>(),
                components = page["components"]
            });
        });

        // Bayraklar + sürüm: uygulama açılışında tek çağrı.
        group.MapGet("/", async (HttpContext http, ITenantContext tenant, ExperienceService experience, CancellationToken ct) =>
        {
            if (tenant.TenantId is not { } tenantId) return Unauthorized();
            var snap = await experience.GetAsync(tenantId, ct);
            if (snap is null)
                return Results.Problem("Bu mağaza için yayınlanmış içerik yok.",
                    statusCode: StatusCodes.Status404NotFound, title: "experience.not_published");

            var screens = (snap.Root["pages"] as JsonArray)?
                .Select(p => new
                {
                    screenType = p?["screenType"]?.GetValue<string>(),
                    handle = p?["handle"]?.GetValue<string>(),
                    name = p?["name"]?.GetValue<string>()
                }).ToList();

            http.Response.Headers.ETag = $"\"v{snap.Version}\"";
            return Results.Ok(new
            {
                experienceVersion = snap.Version,
                generatedAt = snap.Root["generatedAt"]?.GetValue<string>(),
                flags = snap.Root["flags"],
                screens
            });
        });
    }

    // --- Katalog / arama ---
    private static void MapCatalog(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/products").WithTags("Mobile.Catalog");

        group.MapGet("/", async (IStoreClient store, CancellationToken ct,
            string? search = null, string? vendor = null, decimal? minPrice = null, decimal? maxPrice = null,
            string? sort = null, int page = 1, int pageSize = 20) =>
        {
            var all = await store.GetProductsAsync(ct);
            IEnumerable<StoreProduct> q = all.Where(p => p.Status == "active");

            if (!string.IsNullOrWhiteSpace(search))
                q = q.Where(p => p.Title.Contains(search, StringComparison.OrdinalIgnoreCase)
                              || (p.Vendor?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                              || p.Variants.Any(v => (v.Sku ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)
                                                  || (v.Barcode ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)));

            if (!string.IsNullOrWhiteSpace(vendor))
                q = q.Where(p => string.Equals(p.Vendor, vendor, StringComparison.OrdinalIgnoreCase));

            if (minPrice is { } min) q = q.Where(p => MinPrice(p) >= min);
            if (maxPrice is { } max) q = q.Where(p => MinPrice(p) <= max);

            q = sort?.ToLowerInvariant() switch
            {
                "price_asc" => q.OrderBy(MinPrice),
                "price_desc" => q.OrderByDescending(MinPrice),
                "title" => q.OrderBy(p => p.Title),
                _ => q.OrderBy(p => p.Title)
            };

            var total = q.Count();
            var p2 = Math.Max(1, page);
            var size = Math.Clamp(pageSize, 1, 100);
            var items = q.Skip((p2 - 1) * size).Take(size).Select(ToListDto).ToList();

            return Results.Ok(new { total, page = p2, pageSize = size, items });
        });

        group.MapGet("/{productId:long}", async (long productId, ClaimsPrincipal user, ITenantContext tenant,
            IStoreClient store, MobileDbContext db, CancellationToken ct) =>
        {
            var p = await store.GetProductAsync(productId, ct);
            if (p is null)
                return Results.Problem($"Ürün bulunamadı: {productId}", statusCode: StatusCodes.Status404NotFound,
                    title: "product.not_found");

            // Ürün detayı görüntülendi → "son gezilenler" listesine yaz (kişiselleştirmeyi besler).
            if (tenant.TenantId is { } tenantId)
                await RecordViewAsync(db, tenantId, UserOf(user), productId, ct);

            return Results.Ok(ToDetailDto(p));
        });

        var collections = api.MapGroup("/collections").WithTags("Mobile.Catalog");

        collections.MapGet("/", async (IStoreClient store, CancellationToken ct) =>
        {
            var cols = await store.GetCollectionsAsync(ct);
            return Results.Ok(cols.Select(c => new { c.CollectionId, c.Title, c.Handle, productCount = c.ProductIds.Count }));
        });

        collections.MapGet("/{collectionId:long}/products", async (long collectionId, IStoreClient store, CancellationToken ct) =>
        {
            var cols = await store.GetCollectionsAsync(ct);
            var col = cols.FirstOrDefault(c => c.CollectionId == collectionId);
            if (col is null)
                return Results.Problem($"Koleksiyon bulunamadı: {collectionId}",
                    statusCode: StatusCodes.Status404NotFound, title: "collection.not_found");

            var all = await store.GetProductsAsync(ct);
            var items = all.Where(p => col.ProductIds.Contains(p.ProductId)).Select(ToListDto).ToList();
            return Results.Ok(new { col.CollectionId, col.Title, col.Handle, items });
        });
    }

    // --- Favoriler / son gezilenler ---
    private static void MapUserLists(RouteGroupBuilder api)
    {
        var fav = api.MapGroup("/favorites").WithTags("Mobile.User");

        fav.MapGet("/", async (ClaimsPrincipal user, IStoreClient store, MobileDbContext db, CancellationToken ct) =>
        {
            var userRef = UserOf(user);
            var ids = await db.Favorites.Where(f => f.UserRef == userRef)
                .OrderByDescending(f => f.CreatedAt).Select(f => f.ShopifyProductId).ToListAsync(ct);
            var all = await store.GetProductsAsync(ct);
            var items = ids.Select(id => all.FirstOrDefault(p => p.ProductId == id))
                .Where(p => p is not null).Select(p => ToListDto(p!)).ToList();
            return Results.Ok(items);
        });

        fav.MapPost("/", async (FavoriteRequest req, ClaimsPrincipal user, ITenantContext tenant,
            MobileDbContext db, CancellationToken ct) =>
        {
            if (tenant.TenantId is not { } tenantId) return Unauthorized();
            var userRef = UserOf(user);
            var exists = await db.Favorites.AnyAsync(f => f.UserRef == userRef && f.ShopifyProductId == req.ProductId, ct);
            if (!exists)
            {
                db.Favorites.Add(new FavoriteProduct { TenantId = tenantId, UserRef = userRef, ShopifyProductId = req.ProductId });
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok(new { added = true, req.ProductId });
        });

        fav.MapDelete("/{productId:long}", async (long productId, ClaimsPrincipal user, MobileDbContext db, CancellationToken ct) =>
        {
            var userRef = UserOf(user);
            var row = await db.Favorites.FirstOrDefaultAsync(f => f.UserRef == userRef && f.ShopifyProductId == productId, ct);
            if (row is null) return Results.NotFound();
            db.Favorites.Remove(row);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        api.MapGet("/recently-viewed", async (ClaimsPrincipal user, IStoreClient store, MobileDbContext db,
            CancellationToken ct, int limit = 10) =>
        {
            var userRef = UserOf(user);
            var ids = await db.RecentlyViewed.Where(r => r.UserRef == userRef)
                .OrderByDescending(r => r.ViewedAt).Take(Math.Clamp(limit, 1, 50))
                .Select(r => r.ShopifyProductId).ToListAsync(ct);
            var all = await store.GetProductsAsync(ct);
            var items = ids.Select(id => all.FirstOrDefault(p => p.ProductId == id))
                .Where(p => p is not null).Select(p => ToListDto(p!)).ToList();
            return Results.Ok(items);
        }).WithTags("Mobile.User");
    }

    // --- Shopify Checkout yönlendirmesi ---
    private static void MapCheckout(RouteGroupBuilder api)
    {
        api.MapPost("/checkout", async (CheckoutRequest req, IStoreClient store, CancellationToken ct) =>
        {
            if (req.Items is null || req.Items.Count == 0)
                return Results.Problem("Sepet boş.", statusCode: StatusCodes.Status400BadRequest, title: "checkout.empty");
            if (req.Items.Any(i => i.Quantity <= 0))
                return Results.Problem("Adet 0'dan büyük olmalı.", statusCode: StatusCodes.Status400BadRequest, title: "checkout.invalid_quantity");

            var integration = await store.GetIntegrationAsync(ct);
            if (integration is null || string.IsNullOrWhiteSpace(integration.ShopDomain))
                return Results.Problem("Mağaza Shopify bağlantısı bulunamadı.",
                    statusCode: StatusCodes.Status409Conflict, title: "checkout.no_integration");

            // Shopify sepet bağlantısı: ödeme ve sipariş oluşturma Shopify Checkout'ta tamamlanır.
            var cart = string.Join(",", req.Items.Select(i => $"{i.VariantId}:{i.Quantity}"));
            var url = $"https://{integration.ShopDomain}/cart/{cart}";

            return Results.Ok(new
            {
                checkoutUrl = url,
                itemCount = req.Items.Sum(i => i.Quantity),
                note = "Ödeme Shopify Checkout üzerinden tamamlanır."
            });
        }).WithTags("Mobile.Checkout");
    }

    // --- Yardımcılar ---
    private static string UserOf(ClaimsPrincipal user) =>
        user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonim";

    private static IResult Unauthorized() =>
        Results.Problem("Mağaza kapsamı yok.", statusCode: StatusCodes.Status401Unauthorized, title: "tenant.missing");

    private static decimal MinPrice(StoreProduct p) => p.Variants.Count == 0 ? 0 : p.Variants.Min(v => v.Price);

    private static async Task RecordViewAsync(MobileDbContext db, Guid tenantId, string userRef, long productId, CancellationToken ct)
    {
        var row = await db.RecentlyViewed.FirstOrDefaultAsync(r => r.UserRef == userRef && r.ShopifyProductId == productId, ct);
        if (row is null)
            db.RecentlyViewed.Add(new RecentlyViewedProduct { TenantId = tenantId, UserRef = userRef, ShopifyProductId = productId });
        else
            row.ViewedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static MobileProductDto ToListDto(StoreProduct p) => new(
        p.ProductId, p.Title, p.Handle, p.Vendor, p.ProductType, p.ImageUrl,
        MinPrice(p), p.Variants.FirstOrDefault()?.CompareAtPrice,
        p.Variants.Any(v => v.InventoryQuantity > 0), p.Variants.Sum(v => v.InventoryQuantity));

    private static MobileProductDetailDto ToDetailDto(StoreProduct p) => new(
        p.ProductId, p.Title, p.Handle, p.Description, p.Vendor, p.ProductType, p.ImageUrl,
        MinPrice(p), p.Variants.FirstOrDefault()?.CompareAtPrice,
        p.Variants.Any(v => v.InventoryQuantity > 0),
        p.Variants.Select(v => new MobileVariantDto(v.VariantId, v.Sku, v.Barcode, v.Price,
            v.CompareAtPrice, v.InventoryQuantity, v.Title)).ToList());
}
