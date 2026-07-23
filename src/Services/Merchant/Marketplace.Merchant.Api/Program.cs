using FluentValidation;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Outbox;
using Marketplace.BuildingBlocks.Security;
using Marketplace.BuildingBlocks.Web;
using Marketplace.Merchant.Api.Api;
using Marketplace.Merchant.Api.Identity;
using Marketplace.Merchant.Api.Application;
using Marketplace.Merchant.Api.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMarketplaceOpenApi();

// --- Multi-tenancy ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();

// --- EF Core / PostgreSQL ---
builder.Services.AddDbContext<MerchantDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("MerchantDb")));

// --- Secret şifreleme (merchant ödeme/Shopify anahtarları) ---
builder.Services.AddSingleton<ISecretProtector>(
    new AesGcmSecretProtector(builder.Configuration["Secrets:EncryptionKey"]!));

// --- MediatR + FluentValidation ---
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));
builder.Services.AddValidatorsFromAssemblyContaining<CreateMerchantValidator>();

// --- MassTransit / RabbitMQ + Outbox ---
builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();
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
builder.Services.AddOutboxDispatcher<MerchantDbContext>();

// --- Kimlik doğrulama + yetkilendirme (izin matrisi ortak yapı taşında tanımlı) ---
builder.Services.AddKeycloakJwtAuth(builder.Configuration, builder.Environment);

// Panel kullanıcılarının Keycloak üzerinde yönetimi (service account ile).
builder.Services.AddHttpClient<IKeycloakAdminClient, KeycloakAdminClient>(c =>
    c.BaseAddress = new Uri(builder.Configuration["Keycloak:BaseUrl"] ?? "http://keycloak:8080"));

// --- Health checks ---
builder.Services.AddHealthChecks()
    .AddDbContextCheck<MerchantDbContext>("merchant-db");

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<MerchantDbContext>().Database.Migrate();
    app.UseMarketplaceSwaggerUi("Merchant API");
}

app.UseAuthentication();
app.UseTenantResolution();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapMerchantEndpoints();
app.MapUserEndpoints();
app.MapMerchantInternalEndpoints();

app.Run();

public partial class Program;
