using FluentValidation;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Outbox;
using Marketplace.BuildingBlocks.Web;
using Marketplace.Order.Api.Api;
using Marketplace.Order.Api.Application;
using Marketplace.Order.Api.Consumers;
using Marketplace.Order.Api.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMarketplaceOpenApi();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();

builder.Services.AddDbContext<OrderDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("OrderDb")));

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));
builder.Services.AddValidatorsFromAssemblyContaining<CreateOrderValidator>();

builder.Services.AddMassTransit(x =>
{
    // Servise özel kuyruk öneki: farklı servisler aynı consumer adını kullansa bile kuyruklar çakışmaz (fan-out korunur).
    x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("order", includeNamespace: false));
    x.AddConsumer<StockReservedConsumer>();
    x.AddConsumer<StockReservationFailedConsumer>();
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
builder.Services.AddOutboxDispatcher<OrderDbContext>();

builder.Services.AddKeycloakJwtAuth(builder.Configuration, builder.Environment);

builder.Services.AddHealthChecks().AddDbContextCheck<OrderDbContext>("order-db");

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<OrderDbContext>().Database.Migrate();
    app.UseMarketplaceSwaggerUi("Order API");
}

app.UseAuthentication();
app.UseTenantResolution();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapOrderEndpoints();

app.Run();

public partial class Program;
