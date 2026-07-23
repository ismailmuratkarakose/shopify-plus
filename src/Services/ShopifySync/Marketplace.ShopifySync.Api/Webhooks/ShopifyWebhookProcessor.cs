using System.Text.Json;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.ShopifySync.Api.Domain;
using Marketplace.ShopifySync.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.ShopifySync.Api.Webhooks;

/// <summary>
/// Shopify webhook'larını işler: HMAC doğrula → idempotency (webhook-id) → shop→tenant çöz →
/// payload parse → read-model'i (SyncedProduct/Variant) günceller. Shopify kaynak sistemdir;
/// webhook incremental senkron sağlar (pull-sync'in tamamlayıcısı).
/// </summary>
public sealed class ShopifyWebhookProcessor
{
    private readonly ShopifySyncDbContext _db;
    private readonly IStoreContext _scope;
    private readonly IConfiguration _config;
    private readonly ILogger<ShopifyWebhookProcessor> _logger;

    public ShopifyWebhookProcessor(
        ShopifySyncDbContext db,
        IStoreContext scope,
        IConfiguration config,
        ILogger<ShopifyWebhookProcessor> logger)
    {
        _db = db;
        _scope = scope;
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
        var vendor = root.TryGetProperty("vendor", out var vn) ? vn.GetString() : null;
        var productType = root.TryGetProperty("product_type", out var pt) ? pt.GetString() : null;
        var handle = root.TryGetProperty("handle", out var h) ? h.GetString() ?? "" : "";
        var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "active" : "active";
        var updatedAt = root.TryGetProperty("updated_at", out var u) && u.TryGetDateTimeOffset(out var dto)
            ? dto : DateTimeOffset.UtcNow;

        var variants = new List<SyncedVariant>();
        if (root.TryGetProperty("variants", out var vs) && vs.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in vs.EnumerateArray())
            {
                decimal price = 0;
                if (v.TryGetProperty("price", out var p))
                    price = p.ValueKind == JsonValueKind.String ? decimal.Parse(p.GetString()!) : p.GetDecimal();
                variants.Add(new SyncedVariant
                {
                    ShopifyVariantId = v.TryGetProperty("id", out var vid) ? vid.GetInt64() : 0,
                    Sku = v.TryGetProperty("sku", out var s) ? s.GetString() : null,
                    Barcode = v.TryGetProperty("barcode", out var bc) ? bc.GetString() : null,
                    Price = price,
                    InventoryQuantity = v.TryGetProperty("inventory_quantity", out var iq) ? iq.GetInt32() : 0,
                    Title = v.TryGetProperty("title", out var vt) ? vt.GetString() : null
                });
            }
        }

        // Robust upsert: varsa sil (cascade variant) + SaveChanges, sonra taze ekle (pull-sync ile aynı desen).
        var existing = await _db.SyncedProducts.Include(x => x.Variants)
            .FirstOrDefaultAsync(x => x.ShopifyProductId == shopifyProductId, ct);
        if (existing is not null)
        {
            _db.SyncedProducts.Remove(existing);
            await _db.SaveChangesAsync(ct);
        }

        _db.SyncedProducts.Add(new SyncedProduct
        {
            StoreId = pre.StoreId,
            ShopifyProductId = shopifyProductId,
            Title = title,
            Description = description,
            Vendor = vendor,
            ProductType = productType,
            Handle = string.IsNullOrEmpty(handle) ? title.ToLowerInvariant().Replace(' ', '-') : handle,
            Status = status,
            ShopifyUpdatedAt = updatedAt,
            Variants = variants
        });

        await CompleteAsync(pre, ct);
        _logger.LogInformation("Shopify inbound ürün → read-model: shopifyId={Id} tenant={Tenant}", shopifyProductId, pre.StoreId);
        return Results.Ok(new { status = "processed", shopifyProductId });
    }

    public async Task<IResult> HandleInventoryAsync(HttpContext ctx, CancellationToken ct)
    {
        var pre = await BeginAsync(ctx, ct);
        if (pre.Failure is not null) return pre.Failure;
        if (pre.Duplicate) return Results.Ok(new { status = "duplicate" });

        using var doc = JsonDocument.Parse(pre.RawBody);
        var root = doc.RootElement;

        var sku = root.TryGetProperty("sku", out var s) ? s.GetString() ?? "" : "";
        var available = root.TryGetProperty("available", out var a) ? a.GetInt32() : 0;
        if (string.IsNullOrEmpty(sku))
            return Results.BadRequest(new { message = "SKU'suz stok webhook'u (inventory_item_id eşlemesi ileride)." });

        var product = await _db.SyncedProducts.Include(x => x.Variants)
            .FirstOrDefaultAsync(x => x.Variants.Any(v => v.Sku == sku), ct);
        var variant = product?.Variants.FirstOrDefault(v => v.Sku == sku);
        if (variant is null)
            return Results.NotFound(new { message = $"SKU için read-model varyantı yok: {sku}" });

        variant.InventoryQuantity = available;
        await CompleteAsync(pre, ct);
        _logger.LogInformation("Shopify inbound stok → read-model: sku={Sku} available={Qty} tenant={Tenant}", sku, available, pre.StoreId);
        return Results.Ok(new { status = "processed", sku, available });
    }

    // --- Ortak preamble: HMAC + idempotency + shop→mağaza ---

    private sealed class Preamble
    {
        public IResult? Failure { get; init; }
        public bool Duplicate { get; init; }
        public byte[] RawBody { get; init; } = [];
        public Guid StoreId { get; init; }
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

        _scope.SetStore(integration.StoreId, isPlatformScope: false);
        return new Preamble { RawBody = raw, StoreId = integration.StoreId, WebhookId = webhookId, Topic = topic };
    }

    private async Task CompleteAsync(Preamble pre, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(pre.WebhookId))
            _db.WebhookInbox.Add(new WebhookInbox { WebhookId = pre.WebhookId, Topic = pre.Topic });
        await _db.SaveChangesAsync(ct);
    }
}
