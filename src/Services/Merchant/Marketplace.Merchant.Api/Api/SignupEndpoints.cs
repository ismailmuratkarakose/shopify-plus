using Marketplace.BuildingBlocks.Web;
using Marketplace.Merchant.Api.Domain;
using Marketplace.Merchant.Api.Identity;
using Marketplace.Merchant.Api.Infrastructure;
using Marketplace.BuildingBlocks.Outbox;
using Marketplace.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Merchant.Api.Api;

public record StoreSignupRequest(
    string StoreName, string AdminUsername, string AdminEmail,
    string? FirstName, string? LastName, string? Password);

/// <summary>
/// Mağaza self-service kaydı. ANONİM bir uçtur (henüz hesap yoktur) — bu nedenle hız sınırına tabidir.
///
/// Akış: kullanıcı adı/e-posta müsaitlik kontrolü → mağaza kaydı (Pending) → Keycloak'ta
/// mağaza yöneticisi kullanıcısı (tenant_id ile). İki ayrı sistem yazıldığı için ikinci adım
/// başarısız olursa ilk adım TELAFİ edilir; yarım kalmış mağaza bırakılmaz.
///
/// Komisyon oranını mağaza kendisi belirleyemez; platform varsayılanı uygulanır.
/// </summary>
public static class SignupEndpoints
{
    public const string RateLimitPolicy = "signup";

    public static IEndpointRouteBuilder MapSignupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/signup").AllowAnonymous().WithTags("Signup");

        group.MapPost("/", async (StoreSignupRequest req, MerchantDbContext db,
            IKeycloakAdminClient admin, IConfiguration config, ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("StoreSignup");

            if (string.IsNullOrWhiteSpace(req.StoreName) || req.StoreName.Trim().Length < 2)
                return Problem("Mağaza adı en az 2 karakter olmalıdır.", StatusCodes.Status400BadRequest, "signup.invalid_store_name");
            if (string.IsNullOrWhiteSpace(req.AdminUsername) || req.AdminUsername.Trim().Length < 3)
                return Problem("Kullanıcı adı en az 3 karakter olmalıdır.", StatusCodes.Status400BadRequest, "signup.invalid_username");
            if (string.IsNullOrWhiteSpace(req.AdminEmail) || !req.AdminEmail.Contains('@'))
                return Problem("Geçerli bir e-posta adresi gereklidir.", StatusCodes.Status400BadRequest, "signup.invalid_email");

            var storeName = req.StoreName.Trim();
            var username = req.AdminUsername.Trim();
            var slug = Slugify(storeName);

            // 1) Ön kontroller — çakışmayı kullanıcı oluşturmadan önce yakala.
            if (await db.Merchants.IgnoreQueryFilters().AnyAsync(m => m.Slug == slug, ct))
                return Problem($"Bu mağaza adı kullanılıyor: '{storeName}'", StatusCodes.Status409Conflict, "signup.store_exists");

            if (await admin.ExistsAsync(username, req.AdminEmail, ct))
                return Problem("Bu kullanıcı adı veya e-posta zaten kayıtlı.", StatusCodes.Status409Conflict, "signup.user_exists");

            // 2) Mağaza kaydı — Shopify bağlanana kadar Pending.
            var commissionRate = config.GetValue<decimal?>("Platform:DefaultCommissionRate") ?? 0.10m;
            var store = new Domain.Merchant
            {
                Id = Guid.NewGuid(),
                Name = storeName,
                Slug = slug,
                Status = MerchantStatus.Pending,
                CommissionRate = commissionRate
            };
            db.Merchants.Add(store);
            db.EnqueueIntegrationEvent(new MerchantRegisteredIntegrationEvent
            {
                TenantId = store.Id,
                MerchantId = store.Id,
                Name = store.Name,
                Slug = store.Slug,
                CommissionRate = store.CommissionRate
            });
            await db.SaveChangesAsync(ct);

            // 3) Yönetici kullanıcısı — başarısız olursa mağaza kaydı geri alınır.
            PanelUser adminUser;
            try
            {
                adminUser = await admin.CreateStoreUserAsync(store.Id,
                    new CreatePanelUser(username, req.AdminEmail, req.FirstName, req.LastName,
                        Roles.StoreAdmin, req.Password, PasswordIsTemporary: false), ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Kayıt sırasında yönetici kullanıcı oluşturulamadı; mağaza kaydı geri alınıyor: {Store}", slug);
                try
                {
                    db.Merchants.Remove(store);
                    await db.SaveChangesAsync(ct);
                }
                catch (Exception cleanupEx)
                {
                    // Telafi da başarısızsa kaydı elle temizlemek gerekir — açıkça logla.
                    logger.LogError(cleanupEx, "TELAFİ BAŞARISIZ: sahipsiz mağaza kaydı kaldı: {StoreId} ({Slug})", store.Id, slug);
                }

                return Problem("Kayıt tamamlanamadı: yönetici kullanıcı oluşturulamadı. Lütfen tekrar deneyin.",
                    StatusCodes.Status502BadGateway, "signup.user_creation_failed");
            }

            logger.LogInformation("Yeni mağaza kaydı: {Store} ({StoreId}) yönetici={User}", slug, store.Id, username);

            return Results.Created($"/api/merchants/{store.Id}", new
            {
                storeId = store.Id,
                storeName = store.Name,
                slug = store.Slug,
                status = store.Status.ToString(),
                adminUsername = adminUser.Username,
                nextStep = new
                {
                    action = "connect_shopify",
                    description = "Mağazanızı etkinleştirmek için Shopify mağazanızı bağlayın.",
                    endpoint = "POST /api/shopify/connect"
                }
            });
        })
        .RequireRateLimiting(RateLimitPolicy);

        // Kayıt formunun anlık doğrulaması için (aynı hız sınırına tabi).
        group.MapGet("/availability", async (string? storeName, string? username, string? email,
            MerchantDbContext db, IKeycloakAdminClient admin, CancellationToken ct) =>
        {
            bool? storeAvailable = null;
            if (!string.IsNullOrWhiteSpace(storeName))
            {
                var slug = Slugify(storeName.Trim());
                storeAvailable = !await db.Merchants.IgnoreQueryFilters().AnyAsync(m => m.Slug == slug, ct);
            }

            bool? userAvailable = null;
            if (!string.IsNullOrWhiteSpace(username) || !string.IsNullOrWhiteSpace(email))
                userAvailable = !await admin.ExistsAsync(username ?? "", email, ct);

            return Results.Ok(new { storeAvailable, userAvailable });
        })
        .RequireRateLimiting(RateLimitPolicy);

        return app;
    }

    private static string Slugify(string name)
    {
        var slug = new string(name.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }

    private static IResult Problem(string detail, int status, string title) =>
        Results.Problem(detail, statusCode: status, title: title);
}
