using FluentValidation;
using Marketplace.BuildingBlocks.MultiTenancy;
using Marketplace.BuildingBlocks.Outbox;
using Marketplace.BuildingBlocks.Web;
using Marketplace.Catalog.Api.Api;
using Marketplace.Catalog.Api.Application;
using Marketplace.Catalog.Api.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// --- OpenAPI ---
builder.Services.AddMarketplaceOpenApi();

// --- Multi-tenancy (istek başına) ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();

// --- EF Core / PostgreSQL ---
builder.Services.AddDbContext<CatalogDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("CatalogDb")));

// --- MediatR + FluentValidation ---
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));
builder.Services.AddValidatorsFromAssemblyContaining<CreateProductValidator>();

// --- MassTransit / RabbitMQ (event bus) ---
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

// Transactional outbox: event'leri güvenilir şekilde RabbitMQ'ya teslim eder.
builder.Services.AddOutboxDispatcher<CatalogDbContext>();

// --- Kimlik doğrulama: Keycloak (OIDC/JWT) — ortak yapı taşı ---
builder.Services.AddKeycloakJwtAuth(builder.Configuration, builder.Environment);

// --- Health checks ---
builder.Services.AddHealthChecks()
    .AddDbContextCheck<CatalogDbContext>("catalog-db");

var app = builder.Build();

// Dev: şema otomatik oluşturulur/güncellenir. Prod'da migration ayrı bir job ile uygulanır.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
    app.UseMarketplaceSwaggerUi("Catalog API");

app.UseAuthentication();
app.UseTenantResolution();   // tenant claim'i AuthN'den sonra çözülür
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapProductEndpoints();

app.Run();

public partial class Program;
