using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Marketplace.BuildingBlocks.Web;

/// <summary>Keycloak (OIDC/JWT) doğrulamasının tüm servislerde ortak kurulumu.</summary>
public static class KeycloakAuthExtensions
{
    public static IServiceCollection AddKeycloakJwtAuth(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var kc = configuration.GetSection("Keycloak");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = kc["Authority"];
                options.RequireHttpsMetadata = environment.IsProduction();
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    // Token issuer'ı (ör. localhost) ile içeriden erişilen host (keycloak) farklı olabilir.
                    ValidIssuers = kc.GetSection("ValidIssuers").Get<string[]>() ?? [kc["Authority"]!],
                    ValidateAudience = false, // Keycloak public client token'ında aud yok.
                    NameClaimType = "preferred_username",
                    RoleClaimType = ClaimTypes.Role
                };
                options.Events = new JwtBearerEvents
                {
                    // Keycloak realm rollerini (realm_access.roles) standart role claim'ine düzleştir.
                    OnTokenValidated = ctx =>
                    {
                        if (ctx.Principal?.Identity is ClaimsIdentity id)
                        {
                            var realm = ctx.Principal.FindFirst("realm_access")?.Value;
                            if (!string.IsNullOrEmpty(realm))
                            {
                                using var doc = JsonDocument.Parse(realm);
                                if (doc.RootElement.TryGetProperty("roles", out var roles))
                                    foreach (var role in roles.EnumerateArray())
                                        id.AddClaim(new Claim(id.RoleClaimType, role.GetString()!));
                            }
                        }
                        return Task.CompletedTask;
                    }
                };
            });

        // Ortak izin matrisi (roller → policy'ler) tüm servislerde aynı sözlükle kurulur.
        services.AddMarketplacePolicies();
        return services;
    }
}
