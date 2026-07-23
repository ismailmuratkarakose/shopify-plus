using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.ShopifySync.Api.Shopify;

namespace Marketplace.ShopifySync.Api.Api;

public record ConnectRequest(string Shop);

/// <summary>
/// Shopify mağaza bağlama (OAuth) uçları.
/// - POST /api/shopify/connect: mağaza sahibi (JWT tenant) bağlamayı başlatır.
///     simulator → token üretilir + kaydedilir; graphql → authorize URL döner (istemci yönlendirir).
/// - GET /shopify/oauth/callback: Shopify'ın geri çağırısı (anonim); code→token, state→tenant, kaydeder.
/// </summary>
public static class ShopifyOAuthEndpoints
{
    public static IEndpointRouteBuilder MapShopifyOAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/shopify/connect", async (
            ConnectRequest req,
            IStoreContext scope,
            IShopifyOAuth oauth,
            IMerchantIntegrationWriter writer,
            CancellationToken ct) =>
        {
            if (scope.StoreId is not { } merchantId)
                return Results.Problem("İstek bir mağaza (tenant) kapsamı taşımıyor.",
                    statusCode: StatusCodes.Status401Unauthorized, title: "store.missing");
            if (string.IsNullOrWhiteSpace(req.Shop))
                return Results.Problem("Mağaza (shop) gerekli.", statusCode: StatusCodes.Status400BadRequest, title: "shop.required");

            var shop = ShopDomain.Normalize(req.Shop);

            if (!oauth.IsSimulator)
                return Results.Ok(new { mode = "graphql", authorizeUrl = oauth.BuildInstallUrl(shop, merchantId.ToString()) });

            // Simulator: harici yönlendirme olmadan token üret + kaydet.
            var token = await oauth.ExchangeCodeAsync(shop, "sim-code", ct);
            await writer.SaveShopifyAsync(merchantId, shop, token, ct);
            return Results.Ok(new { mode = "simulator", connected = true, shop });
        })
        .RequireAuthorization()
        .WithTags("ShopifySync");

        // Shopify'ın geri çağırısı — anonim (harici); prod'da public URL + HMAC doğrulaması.
        app.MapGet("/shopify/oauth/callback", async (
            string shop, string code, string state,
            IShopifyOAuth oauth,
            IMerchantIntegrationWriter writer,
            CancellationToken ct) =>
        {
            if (!Guid.TryParse(state, out var merchantId))
                return Results.BadRequest(new { message = "Geçersiz state." });

            var s = ShopDomain.Normalize(shop);
            var token = await oauth.ExchangeCodeAsync(s, code, ct);
            await writer.SaveShopifyAsync(merchantId, s, token, ct);
            return Results.Ok(new { connected = true, shop = s });
        })
        .AllowAnonymous()
        .ExcludeFromDescription();

        return app;
    }
}
