using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Outbox;
using Marketplace.BuildingBlocks.Web;
using Marketplace.Inventory.Api.Api;
using Marketplace.Inventory.Api.Consumers;
using Marketplace.Inventory.Api.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMarketplaceOpenApi();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();

builder.Services.AddDbContext<InventoryDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("InventoryDb")));

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// --- MassTransit: consumer'lar + RabbitMQ ---
builder.Services.AddMassTransit(x =>
{
    // Servise özel kuyruk öneki: farklı servisler aynı consumer adını kullansa bile kuyruklar çakışmaz (fan-out korunur).
    x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("inventory", includeNamespace: false));
    x.AddConsumer<ProductCreatedConsumer>();
    x.AddConsumer<OrderPlacedConsumer>();
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
builder.Services.AddOutboxDispatcher<InventoryDbContext>();

builder.Services.AddKeycloakJwtAuth(builder.Configuration, builder.Environment);

builder.Services.AddHealthChecks().AddDbContextCheck<InventoryDbContext>("inventory-db");

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.Migrate();
    app.UseMarketplaceSwaggerUi("Inventory API");
}

app.UseAuthentication();
app.UseTenantResolution();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapInventoryEndpoints();

app.Run();

public partial class Program;
