using Marketplace.Bff.Mobile.Api.Api;
using Marketplace.Bff.Mobile.Api.Cart;
using Marketplace.Bff.Mobile.Api.Clients;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Web;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// --- OpenAPI ---
builder.Services.AddMarketplaceOpenApi();

// --- Multi-tenancy (istek başına; sepet anahtarı tenant kapsamına göre üretilir) ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();

// --- Redis (sepet durumu) ---
var redisConfig = ConfigurationOptions.Parse(builder.Configuration["Redis:Configuration"] ?? "localhost:6379");
redisConfig.AbortOnConnectFail = false; // Redis geç ayağa kalksa da BFF başlar.
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConfig));
builder.Services.AddScoped<ICartStore, RedisCartStore>();

// --- Downstream servis çağrıları: gelen JWT'yi taşıyan typed HttpClient'lar ---
builder.Services.AddTransient<AuthForwardingHandler>();

void AddDownstream<TClient, TImpl>(string serviceKey)
    where TClient : class where TImpl : class, TClient
    => builder.Services.AddHttpClient<TClient, TImpl>(c =>
            c.BaseAddress = new Uri(builder.Configuration[$"Services:{serviceKey}"]!))
        .AddHttpMessageHandler<AuthForwardingHandler>();

AddDownstream<ICatalogApi, CatalogApiClient>("Catalog");
AddDownstream<IOrderApi, OrderApiClient>("Order");

// --- Kimlik doğrulama: Keycloak (OIDC/JWT) — ortak yapı taşı ---
builder.Services.AddKeycloakJwtAuth(builder.Configuration, builder.Environment);

builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.UseMarketplaceSwaggerUi("Mobile BFF API");

app.UseAuthentication();
app.UseTenantResolution();   // tenant claim'i AuthN'den sonra çözülür
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapMobileEndpoints();

app.Run();

public partial class Program;
