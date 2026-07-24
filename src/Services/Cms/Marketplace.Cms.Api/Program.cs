using Marketplace.BuildingBlocks.Auditing;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Web;
using Marketplace.Cms.Api.Api;
using Marketplace.Cms.Api.Clients;
using Marketplace.Cms.Api.Experience;
using Marketplace.Cms.Api.Infrastructure;
using Marketplace.Cms.Api.Storage;
using Marketplace.Cms.Api.Validation;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMarketplaceOpenApi();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IStoreContext, StoreContext>();

builder.Services.AddDbContext<CmsDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("CmsDb")));

// Medya deposu: yerel dosya (geliştirme). S3/MinIO aynı arayüzün arkasına eklenebilir.
builder.Services.AddSingleton<IMediaStorage, LocalFileMediaStorage>();

// İçerik bütünlüğü doğrulaması (R4): ürün/kategori referansları ortak Katalog'dan (anonim),
// indirim kodları Shopify read-model'inden (kullanıcının JWT'si taşınır) doğrulanır.
builder.Services.AddTransient<AuthForwardingHandler>();
builder.Services.AddHttpClient("catalog", c =>
    c.BaseAddress = new Uri(builder.Configuration["Services:Catalog"] ?? "http://catalog:8080"));
builder.Services.AddHttpClient("shopifysync", c =>
    c.BaseAddress = new Uri(builder.Configuration["Services:ShopifySync"] ?? "http://shopifysync:8080"))
    .AddHttpMessageHandler<AuthForwardingHandler>();
builder.Services.AddScoped<IStoreDataClient, StoreDataClient>();
builder.Services.AddScoped<ContentValidator>();
builder.Services.AddScoped<SnapshotBuilder>();
builder.Services.AddAuditLogging<CmsDbContext>();

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
app.UseStoreResolution();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapPageEndpoints();
app.MapExperienceEndpoints();
app.MapPreviewEndpoints();
app.MapMediaEndpoints();
// İçerik denetim kaydı pazaryeri personelinindir (mağaza yöneticilerinin değil).
app.MapAuditEndpoints<CmsDbContext>(policy: Policies.ContentPublish);

app.Run();

public partial class Program;
