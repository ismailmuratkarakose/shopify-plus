using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Web;
using Marketplace.Cms.Api.Api;
using Marketplace.Cms.Api.Clients;
using Marketplace.Cms.Api.Infrastructure;
using Marketplace.Cms.Api.Storage;
using Marketplace.Cms.Api.Validation;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMarketplaceOpenApi();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();

builder.Services.AddDbContext<CmsDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("CmsDb")));

// Medya deposu: yerel dosya (geliştirme). S3/MinIO aynı arayüzün arkasına eklenebilir.
builder.Services.AddSingleton<IMediaStorage, LocalFileMediaStorage>();

// İçerik bütünlüğü doğrulaması için Shopify read-model'i (kullanıcının JWT'si taşınır).
builder.Services.AddTransient<AuthForwardingHandler>();
builder.Services.AddHttpClient<IStoreDataClient, StoreDataClient>(c =>
    c.BaseAddress = new Uri(builder.Configuration["Services:ShopifySync"] ?? "http://shopifysync:8080"))
    .AddHttpMessageHandler<AuthForwardingHandler>();
builder.Services.AddScoped<ContentValidator>();

builder.Services.AddKeycloakJwtAuth(builder.Configuration, builder.Environment);

builder.Services.AddHealthChecks().AddDbContextCheck<CmsDbContext>("cms-db");

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<CmsDbContext>().Database.Migrate();
    app.UseMarketplaceSwaggerUi("CMS API");
}

app.UseAuthentication();
app.UseTenantResolution();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapPageEndpoints();
app.MapPreviewEndpoints();
app.MapMediaEndpoints();

app.Run();

public partial class Program;
