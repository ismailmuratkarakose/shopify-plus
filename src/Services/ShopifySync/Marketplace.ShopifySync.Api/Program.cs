using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Outbox;
using Marketplace.BuildingBlocks.Security;
using Marketplace.BuildingBlocks.Web;
using Marketplace.ShopifySync.Api.Api;
using Marketplace.ShopifySync.Api.Consumers;
using Marketplace.ShopifySync.Api.Infrastructure;
using Marketplace.ShopifySync.Api.Shopify;
using Marketplace.ShopifySync.Api.Webhooks;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMarketplaceOpenApi();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IStoreContext, StoreContext>();

builder.Services.AddDbContext<ShopifySyncDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("ShopifyDb")));

// Secret şifreleme (Merchant ile aynı anahtar → internal'dan gelen token'ı at-rest şifrele).
builder.Services.AddSingleton<ISecretProtector>(
    new AesGcmSecretProtector(builder.Configuration["Secrets:EncryptionKey"]!));

// Merchant internal credential client (X-Internal-Api-Key ile).
builder.Services.AddHttpClient<IMerchantCredentialClient, MerchantCredentialClient>(c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Merchant:InternalBaseUrl"]!);
    c.DefaultRequestHeaders.Add("X-Internal-Api-Key", builder.Configuration["Internal:ApiKey"]!);
});

builder.Services.AddScoped<ShopifyWebhookProcessor>();
builder.Services.AddScoped<StoreSyncService>();
// Periyodik mutabakat: kaçan webhook'lardan doğabilecek veri farklarını kapatır.
builder.Services.AddHostedService<ReconciliationService>();

// Shopify client + OAuth: config ile simulator / graphql seçilir.
var clientMode = builder.Configuration["Shopify:ClientMode"] ?? "simulator";
if (string.Equals(clientMode, "graphql", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<IShopifyClient, GraphQlShopifyClient>();
    builder.Services.AddHttpClient<IShopifyOAuth, GraphQlShopifyOAuth>();
}
else
{
    builder.Services.AddSingleton<IShopifyClient, SimulatorShopifyClient>();
    builder.Services.AddSingleton<IShopifyOAuth, SimulatorShopifyOAuth>();
}

// OAuth connect/callback entegrasyon config'i Merchant'a internal write ile kaydeder.
builder.Services.AddHttpClient<IMerchantIntegrationWriter, MerchantIntegrationWriter>(c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Merchant:InternalBaseUrl"]!);
    c.DefaultRequestHeaders.Add("X-Internal-Api-Key", builder.Configuration["Internal:ApiKey"]!);
});

builder.Services.AddMassTransit(x =>
{
    // Servise özel kuyruk öneki: farklı servisler aynı consumer adını kullansa bile kuyruklar çakışmaz (fan-out korunur).
    x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("shopify-sync", includeNamespace: false));
    x.AddConsumer<MerchantIntegrationConfiguredConsumer>();
    x.UsingRabbitMq((context, cfg) =>
    {
        var rmq = builder.Configuration.GetSection("RabbitMq");
        cfg.Host(rmq["Host"] ?? "localhost", "/", h =>
        {
            h.Username(rmq["Username"] ?? "guest");
            h.Password(rmq["Password"] ?? "guest");
        });
        cfg.ConfigureEndpoints(context);
    });
});
builder.Services.AddOutboxDispatcher<ShopifySyncDbContext>();

builder.Services.AddKeycloakJwtAuth(builder.Configuration, builder.Environment);

builder.Services.AddHealthChecks().AddDbContextCheck<ShopifySyncDbContext>("shopify-db");

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<ShopifySyncDbContext>().Database.Migrate();
    app.UseMarketplaceSwaggerUi("ShopifySync API");
}

app.UseAuthentication();
app.UseStoreResolution();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapShopifyEndpoints();
app.MapShopifyOAuthEndpoints();
app.MapStoreDataEndpoints();
app.MapShopifyWebhookEndpoints();

app.Run();

public partial class Program;
