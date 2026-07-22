using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Web;
using Marketplace.Reporting.Api.Api;
using Marketplace.Reporting.Api.Consumers;
using Marketplace.Reporting.Api.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMarketplaceOpenApi();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();

builder.Services.AddDbContext<ReportingDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("ReportingDb")));

// --- MassTransit: sadece consumer (event yayınlamaz → outbox yok) ---
builder.Services.AddMassTransit(x =>
{
    x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("reporting", includeNamespace: false));
    x.AddConsumer<MerchantRegisteredConsumer>();
    x.AddConsumer<OrderPlacedConsumer>();
    x.AddConsumer<PaymentSucceededConsumer>();
    x.AddConsumer<PaymentFailedConsumer>();
    x.UsingRabbitMq((context, cfg) =>
    {
        var rmq = builder.Configuration.GetSection("RabbitMq");
        cfg.Host(rmq["Host"] ?? "localhost", "/", h =>
        {
            h.Username(rmq["Username"] ?? "guest");
            h.Password(rmq["Password"] ?? "guest");
        });
        // Event sırası garanti değil (ör. PaymentSucceeded, OrderPlaced'dan önce gelebilir):
        // satış kaydı henüz yoksa consumer exception atar, kademeli retry ile telafi edilir.
        cfg.UseMessageRetry(r => r.Intervals(2000, 4000, 8000, 15000));
        cfg.ConfigureEndpoints(context);
    });
});

builder.Services.AddKeycloakJwtAuth(builder.Configuration, builder.Environment);

builder.Services.AddHealthChecks().AddDbContextCheck<ReportingDbContext>("reporting-db");

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<ReportingDbContext>().Database.Migrate();
    app.UseMarketplaceSwaggerUi("Reporting API");
}

app.UseAuthentication();
app.UseTenantResolution();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapReportEndpoints();

app.Run();

public partial class Program;
