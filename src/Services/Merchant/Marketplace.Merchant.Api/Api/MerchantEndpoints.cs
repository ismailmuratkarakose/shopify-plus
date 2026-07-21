using Marketplace.BuildingBlocks.Web;
using Marketplace.Merchant.Api.Application;
using MediatR;

namespace Marketplace.Merchant.Api.Api;

public static class MerchantEndpoints
{
    public static IEndpointRouteBuilder MapMerchantEndpoints(this IEndpointRouteBuilder app)
    {
        // --- Owner / platform yönetimi ---
        var owner = app.MapGroup("/api/merchants")
            .RequireAuthorization("owner")
            .WithTags("Merchants (owner)");

        owner.MapPost("/", async (CreateMerchantCommand cmd, ISender sender) =>
        {
            var result = await sender.Send(cmd);
            return result.ToHttpResult(created: true, location: r => $"/api/merchants/{r.Id}");
        });

        owner.MapGet("/", async (ISender sender) =>
            (await sender.Send(new GetMerchantsQuery())).ToHttpResult());

        // --- Merchant self-service ---
        var me = app.MapGroup("/api/merchants/me")
            .RequireAuthorization()
            .WithTags("Merchant (self)");

        me.MapGet("/", async (ISender sender) =>
            (await sender.Send(new GetMyMerchantQuery())).ToHttpResult());

        me.MapGet("/integrations", async (ISender sender) =>
            (await sender.Send(new GetIntegrationsQuery())).ToHttpResult());

        me.MapPut("/integrations/{provider}", async (
            string provider, Dictionary<string, string> config, ISender sender) =>
        {
            var result = await sender.Send(new UpsertIntegrationCommand(provider, config));
            return result.ToHttpResult();
        });

        return app;
    }
}
