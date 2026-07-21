using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Security;
using Marketplace.Contracts;
using Marketplace.ShopifySync.Api.Domain;
using Marketplace.ShopifySync.Api.Infrastructure;
using Marketplace.ShopifySync.Api.Shopify;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.ShopifySync.Api.Consumers;

/// <summary>
/// Merchant bir Shopify entegrasyonu bağladığında: credential'ı Merchant internal API'sinden
/// çeker ve read-model'e (token shared key ile şifreli) yazar. Secret event bus'a düşmez.
/// </summary>
public sealed class MerchantIntegrationConfiguredConsumer : IConsumer<MerchantIntegrationConfiguredIntegrationEvent>
{
    private readonly ShopifySyncDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IMerchantCredentialClient _merchant;
    private readonly ISecretProtector _protector;
    private readonly ILogger<MerchantIntegrationConfiguredConsumer> _logger;

    public MerchantIntegrationConfiguredConsumer(
        ShopifySyncDbContext db,
        ITenantContext tenant,
        IMerchantCredentialClient merchant,
        ISecretProtector protector,
        ILogger<MerchantIntegrationConfiguredConsumer> logger)
    {
        _db = db;
        _tenant = tenant;
        _merchant = merchant;
        _protector = protector;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<MerchantIntegrationConfiguredIntegrationEvent> context)
    {
        var e = context.Message;
        if (!string.Equals(e.Provider, "shopify", StringComparison.OrdinalIgnoreCase))
            return;

        _tenant.SetTenant(e.MerchantId, isPlatformScope: false);

        var config = await _merchant.GetIntegrationConfigAsync(e.MerchantId, "shopify", context.CancellationToken);
        if (config is null || !config.TryGetValue("shopDomain", out var shopDomain) || !config.TryGetValue("accessToken", out var accessToken))
        {
            _logger.LogWarning("Merchant {MerchantId} için Shopify config alınamadı.", e.MerchantId);
            return;
        }

        var integration = await _db.Integrations.FirstOrDefaultAsync(i => i.TenantId == e.MerchantId, context.CancellationToken);
        if (integration is null)
        {
            integration = new ShopifyIntegration { TenantId = e.MerchantId };
            _db.Integrations.Add(integration);
        }
        integration.ShopDomain = shopDomain;
        integration.EncryptedAccessToken = _protector.Protect(accessToken);
        integration.IsActive = e.IsActive;

        await _db.SaveChangesAsync(context.CancellationToken);
        _logger.LogInformation("Shopify entegrasyonu güncellendi: merchant={MerchantId} store={Store} aktif={Active}",
            e.MerchantId, shopDomain, e.IsActive);
    }
}
