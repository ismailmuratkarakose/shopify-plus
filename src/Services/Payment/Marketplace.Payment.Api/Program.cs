using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Outbox;
using Marketplace.BuildingBlocks.Web;
using Marketplace.Payment.Api.Api;
using Marketplace.Payment.Api.Consumers;
using Marketplace.Payment.Api.Infrastructure;
using Marketplace.Payment.Api.Providers;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMarketplaceOpenApi();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();

builder.Services.AddDbContext<PaymentDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("PaymentDb")));

// --- Ödeme sağlayıcıları ---
builder.Services.AddSingleton<SimulatorPaymentProvider>();
builder.Services.AddHttpClient<IyzicoPaymentProvider>();
builder.Services.AddHttpClient<PayPalPaymentProvider>();
builder.Services.AddHttpClient<IMerchantCredentialClient, MerchantCredentialClient>(c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Merchant:InternalBaseUrl"]!);
    c.DefaultRequestHeaders.Add("X-Internal-Api-Key", builder.Configuration["Internal:ApiKey"]!);
});
builder.Services.AddScoped<IPaymentProviderResolver, PaymentProviderResolver>();

builder.Services.AddMassTransit(x =>
{
    x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("payment", includeNamespace: false));
    x.AddConsumer<PaymentRequestedConsumer>();
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
builder.Services.AddOutboxDispatcher<PaymentDbContext>();

builder.Services.AddKeycloakJwtAuth(builder.Configuration, builder.Environment);

builder.Services.AddHealthChecks().AddDbContextCheck<PaymentDbContext>("payment-db");

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Database.Migrate();
    app.UseMarketplaceSwaggerUi("Payment API");
}

app.UseAuthentication();
app.UseTenantResolution();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapPaymentEndpoints();

app.Run();

public partial class Program;
