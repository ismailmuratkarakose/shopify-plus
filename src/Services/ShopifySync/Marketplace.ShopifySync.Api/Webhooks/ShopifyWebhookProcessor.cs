using System.Text.Json;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Outbox;
using Marketplace.Contracts;
using Marketplace.ShopifySync.Api.Domain;
using Marketplace.ShopifySync.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.ShopifySync.Api.Webhooks;

/// <summary>
/// Shopify webhook'larını işler: HMAC doğrula → idempotency (webhook-id) → shop→tenant çöz →
/// payload parse → integration event'i outbox'a yaz. Secret ve idempotency aynı transaction'da.
/// </summary>
public sealed class ShopifyWebhookProcessor
{
    private readonly ShopifySyncDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IConfiguration _config;
    private readonly ILogger<ShopifyWebhookProcessor> _logger;

    public ShopifyWebhookProcessor(
        ShopifySyncDbContext db,
        ITenantContext tenant,
        IConfiguration config,
        ILogger<ShopifyWebhookProcessor> logger)
    {
        _db = db;
        _tenant = tenant;
        _config = config;
        _logger = logger;
    }

    public async Task<IResult> HandleProductUpsertAsync(HttpContext ctx, CancellationToken ct)
    {
        var pre = await BeginAsync(ctx, ct);
        if (pre.Failure is not null) return pre.Failure;
        if (pre.Duplicate) return Results.Ok(new { status = "duplicate" });

        using var doc = JsonDocument.Parse(pre.RawBody);
        var root = doc.RootElement;

        var shopifyProductId = root.GetProperty("id").GetInt64();
        var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var description = root.TryGetProperty("body_html", out var b) ? b.GetString() : null;
        var updatedAt = root.TryGetProperty("updated_at", out var u) && u.TryGetDateTimeOffset(out var dto)
            ? dto : DateTimeOffset.UtcNow;
        var currency = root.TryGetProperty("currency", out var c) ? c.GetString() ?? "TRY" : "TRY";

        string sku = "";
        decimal price = 0;
        if (root.TryGetProperty("variants", out var variants) && variants.GetArrayLength() > 0)
        {
            var v = variants[0];
            sku = v.TryGetProperty("sku", out var s) ? s.GetString() ?? "" : "";
            if (v.TryGetProperty("price", out var p))
                price = p.ValueKind == JsonValueKind.String ? decimal.Parse(p.GetString()!) : p.GetDecimal();
        }

        if (string.IsNullOrEmpty(sku))
            return Results.BadRequest(new { message = "SKU'suz ürün webhook'u atlandı." });

        // Eşlemeyi Sku ile upsert (ShopifyProductId'i tut).
        var mapping = await _db.ProductMappings.FirstOrDefaultAsync(m => m.Sku == sku, ct);
        if (mapping is null)
            _db.ProductMappings.Add(new ProductMapping { TenantId = pre.TenantId, Sku = sku, ShopifyProductId = shopifyProductId });
        else
            mapping.ShopifyProductId = shopifyProductId;

        _db.EnqueueIntegrationEvent(new ProductUpsertedFromShopifyIntegrationEvent
        {
            TenantId = pre.TenantId,
            ShopifyProductId = shopifyProductId,
            Sku = sku,
            Title = title,
            Description = description,
            Price = price,
            Currency = currency,
            ShopifyUpdatedAt = updatedAt
        });

        await CompleteAsync(pre, ct);
        _logger.LogInformation("Shopify inbound ürün: sku={Sku} shopifyId={Id} tenant={Tenant}", sku, shopifyProductId, pre.TenantId);
        return Results.Ok(new { status = "processed", sku });
    }

    public async Task<IResult> HandleInventoryAsync(HttpContext ctx, CancellationToken ct)
    {
        var pre = await BeginAsync(ctx, ct);
        if (pre.Failure is not null) return pre.Failure;
        if (pre.Duplicate) return Results.Ok(new { status = "duplicate" });

        using var doc = JsonDocument.Parse(pre.RawBody);
        var root = doc.RootElement;

        // Gerçek Shopify inventory_levels/update: inventory_item_id + available.
        // Simulator/Faz 2: sku taşır (inventory_item_id→sku eşlemesi gerçek mağazada tutulacak).
        var sku = root.TryGetProperty("sku", out var s) ? s.GetString() ?? "" : "";
        var available = root.TryGetProperty("available", out var a) ? a.GetInt32() : 0;

        if (string.IsNullOrEmpty(sku))
            return Results.BadRequest(new { message = "SKU'suz stok webhook'u (inventory_item_id eşlemesi Faz 2b+)." });

        _db.EnqueueIntegrationEvent(new StockChangedFromShopifyIntegrationEvent
        {
            TenantId = pre.TenantId,
            Sku = sku,
            QuantityOnHand = available
        });

        await CompleteAsync(pre, ct);
        _logger.LogInformation("Shopify inbound stok: sku={Sku} available={Qty} tenant={Tenant}", sku, available, pre.TenantId);
        return Results.Ok(new { status = "processed", sku, available });
    }

    // --- Ortak preamble: HMAC + idempotency + shop→tenant ---

    private sealed class Preamble
    {
        public IResult? Failure { get; init; }
        public bool Duplicate { get; init; }
        public byte[] RawBody { get; init; } = [];
        public Guid TenantId { get; init; }
        public string WebhookId { get; init; } = "";
        public string Topic { get; init; } = "";
    }

    private async Task<Preamble> BeginAsync(HttpContext ctx, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await ctx.Request.Body.CopyToAsync(ms, ct);
        var raw = ms.ToArray();

        var secret = _config["Shopify:WebhookSecret"] ?? "";
        var hmac = ctx.Request.Headers["X-Shopify-Hmac-Sha256"].ToString();
        if (!ShopifyWebhookVerifier.IsValid(raw, hmac, secret))
            return new Preamble { Failure = Results.Unauthorized() };

        var topic = ctx.Request.Headers["X-Shopify-Topic"].ToString();
        var webhookId = ctx.Request.Headers["X-Shopify-Webhook-Id"].ToString();
        if (!string.IsNullOrEmpty(webhookId) &&
            await _db.WebhookInbox.AnyAsync(w => w.WebhookId == webhookId, ct))
            return new Preamble { Duplicate = true };

        var shopDomain = ctx.Request.Headers["X-Shopify-Shop-Domain"].ToString();
        var integration = await _db.Integrations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.ShopDomain == shopDomain && i.IsActive, ct);
        if (integration is null)
            return new Preamble { Failure = Results.NotFound(new { message = $"Bilinmeyen/pasif mağaza: {shopDomain}" }) };

        _tenant.SetTenant(integration.TenantId, isPlatformScope: false);
        return new Preamble { RawBody = raw, TenantId = integration.TenantId, WebhookId = webhookId, Topic = topic };
    }

    private async Task CompleteAsync(Preamble pre, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(pre.WebhookId))
            _db.WebhookInbox.Add(new WebhookInbox { WebhookId = pre.WebhookId, Topic = pre.Topic });
        await _db.SaveChangesAsync(ct);
    }
}
