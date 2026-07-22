using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Web;
using Marketplace.Cms.Api.Api;
using Marketplace.Cms.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMarketplaceOpenApi();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();

builder.Services.AddDbContext<CmsDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("CmsDb")));

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

app.Run();

public partial class Program;
