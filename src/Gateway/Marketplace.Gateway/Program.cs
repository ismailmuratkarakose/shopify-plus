using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

// --- YARP reverse proxy (route/cluster config appsettings'ten) ---
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// --- Kimlik doğrulama: Keycloak (JWT) — gateway sınırında doğrulanır ---
var kc = builder.Configuration.GetSection("Keycloak");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = kc["Authority"];
        options.RequireHttpsMetadata = builder.Environment.IsProduction();
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = kc.GetSection("ValidIssuers").Get<string[]>() ?? [kc["Authority"]!],
            ValidateAudience = false,
            NameClaimType = "preferred_username",
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };
    });
builder.Services.AddAuthorization();

// --- Rate limiting (istemci başına sabit pencere) ---
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var key = ctx.User.Identity?.Name
                  ?? ctx.Connection.RemoteIpAddress?.ToString()
                  ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "gateway" }));

// Tüm eşleşen istekleri arka servislere yönlendir.
app.MapReverseProxy();

app.Run();
