using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Web;
using Marketplace.Mobile.Api.Api;
using Marketplace.Mobile.Api.Clients;
using Marketplace.Mobile.Api.Experience;
using Marketplace.Mobile.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMarketplaceOpenApi();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();

builder.Services.AddDbContext<MobileDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("MobileDb")));

// Downstream servisler: kullanıcının JWT'si taşınır (kiracı kapsamı korunur).
builder.Services.AddTransient<AuthForwardingHandler>();

builder.Services.AddHttpClient<IExperienceClient, ExperienceClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:Cms"] ?? "http://cms:8080"))
    .AddHttpMessageHandler<AuthForwardingHandler>();

builder.Services.AddHttpClient<IStoreClient, StoreClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:ShopifySync"] ?? "http://shopifysync:8080"))
    .AddHttpMessageHandler<AuthForwardingHandler>();

// Deneyim anlık görüntüsü önbelleği (mağaza başına; TTL sonrası ETag ile yeniden doğrulanır).
builder.Services.AddSingleton<ExperienceCache>();
builder.Services.AddScoped<ExperienceService>();

builder.Services.AddKeycloakJwtAuth(builder.Configuration, builder.Environment);

builder.Services.AddHealthChecks().AddDbContextCheck<MobileDbContext>("mobile-db");

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<MobileDbContext>().Database.Migrate();
    app.UseMarketplaceSwaggerUi("Mobile Experience API");
}

app.UseAuthentication();
app.UseTenantResolution();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapMobileEndpoints();

app.Run();

public partial class Program;
