using System.Text.Json;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Security;
using Marketplace.BuildingBlocks.Web;
using Marketplace.Merchant.Api.Application;
using Marketplace.Merchant.Api.Infrastructure;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Merchant.Api.Api;

/// <summary>
/// Servisler arası (internal) endpoint'ler. Gateway'e route EDİLMEZ; sadece küme içinden,
/// paylaşılan X-Internal-Api-Key ile erişilir. Prod'da mTLS / OAuth client-credentials'a döner.
/// Amaç: ShopifySync gibi servislerin merchant credential'ını (decrypted) çekmesi —
/// böylece secret event bus'a hiç düşmez.
/// </summary>
public static class InternalEndpoints
{
    public static IEndpointRouteBuilder MapMerchantInternalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal").WithTags("Internal").ExcludeFromDescription();

        group.MapGet("/integrations/{merchantId:guid}/{provider}", async (
            Guid merchantId,
            string provider,
            HttpContext http,
            IConfiguration config,
            MerchantDbContext db,
            ISecretProtector protector) =>
        {
            // Basit internal auth: paylaşılan API key.
            var expected = config["Internal:ApiKey"];
            var provided = http.Request.Headers["X-Internal-Api-Key"].ToString();
            if (string.IsNullOrEmpty(expected) || provided != expected)
                return Results.Unauthorized();

            // Internal çağrı JWT tenant kapsamı taşımaz → query filter'ı bypass edip merchantId ile filtrele.
            var integration = await db.Integrations
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(i => i.StoreId == merchantId && i.Provider == provider && i.IsActive);

            if (integration is null)
                return Results.NotFound();

            var configDict = JsonSerializer.Deserialize<Dictionary<string, string>>(
                protector.Unprotect(integration.EncryptedConfig)) ?? new();

            return Results.Ok(new
            {
                merchantId,
                provider,
                config = configDict
            });
        });

        // Server-to-server yazma: OAuth callback gibi JWT taşımayan akışlar entegrasyon config'i buradan kaydeder.
        // Tenant'ı merchantId'den kurup mevcut UpsertIntegration handler'ını kullanır (şifreler + event yayınlar).
        group.MapPost("/integrations/{merchantId:guid}/{provider}", async (
            Guid merchantId,
            string provider,
            Dictionary<string, string> config,
            HttpContext http,
            IConfiguration appConfig,
            IStoreContext scope,
            ISender sender) =>
        {
            var expected = appConfig["Internal:ApiKey"];
            var provided = http.Request.Headers["X-Internal-Api-Key"].ToString();
            if (string.IsNullOrEmpty(expected) || provided != expected)
                return Results.Unauthorized();

            // Internal çağrı JWT tenant taşımaz → tenant'ı merchantId'den kur (aynı request scope handler'a taşınır).
            scope.SetStore(merchantId, isPlatformScope: false);
            var result = await sender.Send(new UpsertIntegrationCommand(provider, config));
            return result.ToHttpResult();
        });

        return app;
    }
}
