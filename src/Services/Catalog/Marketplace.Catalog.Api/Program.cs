using Marketplace.BuildingBlocks.Auditing;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Web;
using Marketplace.Catalog.Api.Api;
using Marketplace.Catalog.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMarketplaceOpenApi();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IStoreContext, StoreContext>();

builder.Services.AddDbContext<CatalogDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("CatalogDb")));

builder.Services.AddAuditLogging<CatalogDbContext>();
builder.Services.AddKeycloakJwtAuth(builder.Configuration, builder.Environment);

builder.Services.AddHealthChecks().AddDbContextCheck<CatalogDbContext>("catalog-db");

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.Migrate();
    app.UseMarketplaceSwaggerUi("Catalog API");
}

app.UseAuthentication();
app.UseStoreResolution();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapCategoryEndpoints();
app.MapPublicCatalogEndpoints();
app.MapStoreProductEndpoints();
// Katalog denetim kaydı: mağaza kendi teklif hareketlerini, platform hepsini görür.
app.MapAuditEndpoints<CatalogDbContext>("/api/audit/catalog");

app.Run();

public partial class Program;
