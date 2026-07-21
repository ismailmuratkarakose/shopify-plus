namespace Marketplace.ShopifySync.Api.Webhooks;

public static class WebhookEndpoints
{
    /// <summary>
    /// Shopify webhook alıcıları. Anonim (JWT yok) — güvenlik HMAC imzasıyla sağlanır.
    /// Gateway'e route EDİLMEZ; Shopify doğrudan servisin public URL'ine çağırır (prod'da ingress).
    /// </summary>
    public static IEndpointRouteBuilder MapShopifyWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/webhooks/shopify").WithTags("Webhooks");

        group.MapPost("/products/create",
            (HttpContext ctx, ShopifyWebhookProcessor p, CancellationToken ct) => p.HandleProductUpsertAsync(ctx, ct));

        group.MapPost("/products/update",
            (HttpContext ctx, ShopifyWebhookProcessor p, CancellationToken ct) => p.HandleProductUpsertAsync(ctx, ct));

        group.MapPost("/inventory_levels/update",
            (HttpContext ctx, ShopifyWebhookProcessor p, CancellationToken ct) => p.HandleInventoryAsync(ctx, ct));

        return app;
    }
}
