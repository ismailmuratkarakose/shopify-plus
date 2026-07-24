using System.Security.Claims;
using System.Text.Json.Nodes;
using Marketplace.Mobile.Api.Clients;
using Marketplace.Mobile.Api.Domain;
using Marketplace.Mobile.Api.Experience;
using Marketplace.Mobile.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Mobile.Api.Api;

public record CheckoutItem(long VariantId, int Quantity);
public record CheckoutRequest(List<CheckoutItem> Items);
public record FavoriteRequest(Guid ProductId);

/// <summary>
/// Mobil uygulamanın tükettiği tek giriş noktası: yayınlanan ekran yapılandırması (CMS snapshot'ı),
/// ORTAK katalog (barkod master + satıcı teklifleri — R4) ve kullanıcı listeleri.
/// Deneyim ve katalog KAMUSALDIR (giriş yapmamış pazaryeri müşterisi gezebilir);
/// favoriler/son gezilenler kimlik ister. Checkout R8'de platform ödemesine dönecek.
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
        // Yayınlanan deneyim kamusaldır: giriş yapmamış pazaryeri müşterisi de uygulamayı açabilmeli.
        var group = api.MapGroup("/experience").WithTags("Mobile.Experience").AllowAnonymous();

        group.MapGet("/{screen}", async (string screen, HttpContext http,
            ExperienceService experience, CancellationToken ct) =>
        {
            var snap = await experience.GetAsync(ct);
            if (snap is null)
                return Results.Problem("Henüz yayınlanmış içerik yok.",
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
        group.MapGet("/", async (HttpContext http, ExperienceService experience, CancellationToken ct) =>
        {
            var snap = await experience.GetAsync(ct);
            if (snap is null)
                return Results.Problem("Henüz yayınlanmış içerik yok.",
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

    // --- Katalog / arama (R4: ortak katalog, anonim) ---
    private static void MapCatalog(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/products").WithTags("Mobile.Catalog").AllowAnonymous();

        // Liste/arama: ortak kataloğun kamusal ucuna vekillik eder (en iyi fiyat + satıcı sayısı).
        group.MapGet("/", async (ICatalogClient catalog, CancellationToken ct,
            string? search = null, Guid? categoryId = null, string? sort = null,
            int page = 1, int pageSize = 20) =>
        {
            var result = await catalog.SearchAsync(search, categoryId, sort, page, pageSize, ct);
            return result is null
                ? Results.Problem("Katalog şu anda erişilemez.", statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "catalog.unavailable")
                : Results.Ok(result);
        });

        group.MapGet("/{productId:guid}", async (Guid productId, ClaimsPrincipal user,
            ICatalogClient catalog, MobileDbContext db, CancellationToken ct) =>
        {
            var product = await catalog.GetProductAsync(productId, ct);
            if (product is null)
                return Results.Problem($"Ürün bulunamadı: {productId}", statusCode: StatusCodes.Status404NotFound,
                    title: "product.not_found");

            // Ürün detayı görüntülendi → giriş yapmış kullanıcıda "son gezilenler"e yaz
            // (kişiselleştirmeyi besler). Anonim gezinmede sessizce atlanır.
            if (user.Identity?.IsAuthenticated == true)
                await RecordViewAsync(db, UserOf(user), productId, ct);

            return Results.Ok(product);
        });

        // Kategori ağacı (eski "collections" kavramının ortak katalogdaki karşılığı).
        api.MapGroup("/categories").WithTags("Mobile.Catalog").AllowAnonymous()
            .MapGet("/", async (ICatalogClient catalog, CancellationToken ct) =>
            {
                var cats = await catalog.GetCategoriesAsync(ct);
                return cats is null
                    ? Results.Problem("Katalog şu anda erişilemez.",
                        statusCode: StatusCodes.Status503ServiceUnavailable, title: "catalog.unavailable")
                    : Results.Ok(cats);
            });
    }

    // --- Favoriler / son gezilenler (kimlik ister) ---
    private static void MapUserLists(RouteGroupBuilder api)
    {
        var fav = api.MapGroup("/favorites").WithTags("Mobile.User");

        fav.MapGet("/", async (ClaimsPrincipal user, ICatalogClient catalog, MobileDbContext db, CancellationToken ct) =>
        {
            var userRef = UserOf(user);
            var ids = await db.Favorites.Where(f => f.UserRef == userRef)
                .OrderByDescending(f => f.CreatedAt).Select(f => f.ProductId).ToListAsync(ct);
            var enriched = await catalog.GetByIdsAsync(ids, ct);
            return Results.Ok(enriched?["items"] ?? new JsonArray());
        });

        fav.MapPost("/", async (FavoriteRequest req, ClaimsPrincipal user, MobileDbContext db, CancellationToken ct) =>
        {
            var userRef = UserOf(user);
            var exists = await db.Favorites.AnyAsync(f => f.UserRef == userRef && f.ProductId == req.ProductId, ct);
            if (!exists)
            {
                db.Favorites.Add(new FavoriteProduct { UserRef = userRef, ProductId = req.ProductId });
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok(new { added = true, req.ProductId });
        });

        fav.MapDelete("/{productId:guid}", async (Guid productId, ClaimsPrincipal user, MobileDbContext db, CancellationToken ct) =>
        {
            var userRef = UserOf(user);
            var row = await db.Favorites.FirstOrDefaultAsync(f => f.UserRef == userRef && f.ProductId == productId, ct);
            if (row is null) return Results.NotFound();
            db.Favorites.Remove(row);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        api.MapGet("/recently-viewed", async (ClaimsPrincipal user, ICatalogClient catalog, MobileDbContext db,
            CancellationToken ct, int limit = 10) =>
        {
            var userRef = UserOf(user);
            var ids = await db.RecentlyViewed.Where(r => r.UserRef == userRef)
                .OrderByDescending(r => r.ViewedAt).Take(Math.Clamp(limit, 1, 50))
                .Select(r => r.ProductId).ToListAsync(ct);
            var enriched = await catalog.GetByIdsAsync(ids, ct);
            return Results.Ok(enriched?["items"] ?? new JsonArray());
        }).WithTags("Mobile.User");
    }

    // --- Checkout (ARA DURUM: Shopify sepet bağlantısı — R8'de platform ödemesi/iyzico'ya dönecek) ---
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

            var cart = string.Join(",", req.Items.Select(i => $"{i.VariantId}:{i.Quantity}"));
            var url = $"https://{integration.ShopDomain}/cart/{cart}";

            return Results.Ok(new
            {
                checkoutUrl = url,
                itemCount = req.Items.Sum(i => i.Quantity),
                note = "GEÇİCİ: R8'de platformun kendi ödeme akışına (iyzico) dönecek."
            });
        }).WithTags("Mobile.Checkout");
    }

    // --- Yardımcılar ---
    private static string UserOf(ClaimsPrincipal user) =>
        user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonim";

    private static async Task RecordViewAsync(MobileDbContext db, string userRef, Guid productId, CancellationToken ct)
    {
        var row = await db.RecentlyViewed.FirstOrDefaultAsync(r => r.UserRef == userRef && r.ProductId == productId, ct);
        if (row is null)
            db.RecentlyViewed.Add(new RecentlyViewedProduct { UserRef = userRef, ProductId = productId });
        else
            row.ViewedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
